using Meitou.Modding.Assets;
using Meitou.Modding.Vfs;

namespace Meitou.Tests.Modding;

public class AssetPipelineTests
{
    sealed class TestAsset(string path) : IDisposable
    {
        public string Path { get; } = path;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    sealed class TestImporter : IAssetImporter
    {
        public bool CanImport(ModAssetFile file) => file.Kind == AssetKind.Texture;
        public object Import(Stream stream, ModAssetFile file) => new TestAsset(file.RelativePath);
    }

    static (ModTempFolder Root, AssetPipeline Pipeline, AssetId Id) Setup()
    {
        var root = new ModTempFolder();
        root.Write("Mod/assets/texture.png", "png");
        root.Write("Mod/assets/mesh.mesh", "mesh");
        var manifest = ModFixtures.Manifest("frontier.mod");
        manifest.Assets = ["assets/"];
        var vfs = ModVfs.Mount([ModFixtures.CandidateAt("Mod", System.IO.Path.Combine(root.Path, "Mod"), manifest)]);
        var pipeline = new AssetPipeline(vfs);
        pipeline.Register(new TestImporter());
        return (root, pipeline, new AssetId("frontier.mod", "texture.png"));
    }

    [Fact]
    public void Caches_and_reference_counts_imported_assets()
    {
        var (root, pipeline, id) = Setup();
        using var _ = root;

        var first = pipeline.Acquire<TestAsset>(id);
        var second = pipeline.Acquire<TestAsset>(id);
        Assert.Same(first.Value, second.Value);

        first.Dispose();
        Assert.False(second.Value.Disposed);
        second.Dispose();
        Assert.True(second.Value.Disposed);

        var third = pipeline.Acquire<TestAsset>(id);
        Assert.NotSame(second.Value, third.Value);
        third.Dispose();
    }

    [Fact]
    public void Double_dispose_releases_only_once()
    {
        var (root, pipeline, id) = Setup();
        using var _ = root;

        var handle = pipeline.Acquire<TestAsset>(id);
        handle.Dispose();
        handle.Dispose();

        var again = pipeline.Acquire<TestAsset>(id);
        Assert.NotSame(handle.Value, again.Value);
        again.Dispose();
    }

    [Fact]
    public void Missing_asset_and_missing_importer_are_distinct_errors()
    {
        var (root, pipeline, _) = Setup();
        using var _root = root;

        Assert.Throws<FileNotFoundException>(() => pipeline.Acquire<TestAsset>(new AssetId("frontier.mod", "missing.png")));
        Assert.Throws<NotSupportedException>(() => pipeline.Acquire<TestAsset>(new AssetId("frontier.mod", "mesh.mesh")));
        Assert.Throws<InvalidOperationException>(() => pipeline.Acquire<string>(new AssetId("frontier.mod", "texture.png")));
    }
}
