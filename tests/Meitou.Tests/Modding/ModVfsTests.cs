using Meitou.Modding;
using Meitou.Modding.Assets;
using Meitou.Modding.Content;
using Meitou.Modding.Vfs;

namespace Meitou.Tests.Modding;

public class AssetIdTests
{
    [Fact]
    public void Parses_mod_scoped_paths_and_keeps_separators_normalized()
    {
        Assert.True(AssetId.TryParse("frontier.revolver:weapons/revolver.png", out var id));
        Assert.Equal("frontier.revolver", id.Mod);
        Assert.Equal("weapons/revolver.png", id.Path);
        Assert.Equal("frontier.revolver:weapons/revolver.png", id.ToString());

        Assert.True(AssetId.TryParse(@"frontier.revolver:weapons\revolver.png", out var windows));
        Assert.Equal(id, windows);
    }

    [Theory]
    [InlineData("")]
    [InlineData("frontier.revolver")]
    [InlineData(":weapons/revolver.png")]
    [InlineData("frontier.revolver:")]
    [InlineData("frontier.revolver:../revolver.png")]
    [InlineData("frontier.revolver:/revolver.png")]
    [InlineData("frontier.revolver:weapons/")]
    public void Rejects_invalid_asset_ids(string text) => Assert.False(AssetId.TryParse(text, out _));

    [Theory]
    [InlineData("a.png", AssetKind.Texture)]
    [InlineData("a.dds", AssetKind.Texture)]
    [InlineData("a.MESH", AssetKind.Mesh)]
    [InlineData("a.wav", AssetKind.Audio)]
    [InlineData("a.ttf", AssetKind.Font)]
    [InlineData("a.yaml", AssetKind.Data)]
    [InlineData("a.xyz", AssetKind.Other)]
    public void Infers_asset_kinds_from_extensions(string path, AssetKind kind) =>
        Assert.Equal(kind, AssetKinds.FromPath(path));
}

public class ContentIdTests
{
    [Fact]
    public void Parses_full_and_local_ids()
    {
        Assert.True(ContentId.TryParse("frontier.revolver:item/revolver", out var id));
        Assert.Equal("frontier.revolver", id.Mod);
        Assert.Equal("item", id.Type);
        Assert.Equal("revolver", id.Name);

        Assert.True(ContentId.TryLocal("frontier.revolver", "item/revolver", out var local));
        Assert.Equal(id, local);
        Assert.Equal("frontier.revolver:item/revolver", id.ToString());
    }

    [Theory]
    [InlineData("frontier.revolver:Item/revolver")]
    [InlineData("frontier.revolver:item/")]
    [InlineData("frontier.revolver:/revolver")]
    [InlineData("frontier.revolver:item/revolver/extra")]
    [InlineData("frontier:item/revolver")]
    [InlineData("frontier.revolver")]
    public void Rejects_invalid_content_ids(string text) => Assert.False(ContentId.TryParse(text, out _));
}

public class ModVfsTests
{
    static ModManifest Manifest(string id, params string[] assets)
    {
        var manifest = ModFixtures.Manifest(id);
        manifest.Assets = [.. assets];
        return manifest;
    }

    static ModCandidate Candidate(ModTempFolder root, string folder, ModManifest manifest) =>
        ModFixtures.CandidateAt(folder, System.IO.Path.Combine(root.Path, folder), manifest);

    [Fact]
    public void Mounts_assets_and_content_roots()
    {
        using var root = new ModTempFolder();
        root.Write("Revolver/assets/weapons/revolver.png", "png");
        root.Write("Revolver/assets/weapons/revolver.mesh", "mesh");
        root.Write("Revolver/content/items/revolver.yaml", "id: revolver");
        var manifest = Manifest("frontier.revolver", "assets/");
        manifest.Content = ["content/"];

        var vfs = ModVfs.Mount([Candidate(root, "Revolver", manifest)]);

        Assert.Empty(vfs.Issues);
        Assert.Equal(2, vfs.Assets.Count);
        Assert.True(vfs.Find(new AssetId("frontier.revolver", "weapons/revolver.png")) is { Kind: AssetKind.Texture });
        Assert.Equal("items/revolver.yaml", Assert.Single(vfs.ContentFiles).RelativePath);
    }

    [Fact]
    public void Later_mod_overrides_a_relative_asset_path_but_ids_stay_scoped()
    {
        using var root = new ModTempFolder();
        root.Write("A/assets/shared.png", "a");
        root.Write("B/assets/shared.png", "b");
        var a = Candidate(root, "A", Manifest("frontier.a", "assets/"));
        var b = Candidate(root, "B", Manifest("frontier.b", "assets/"));

        var vfs = ModVfs.Mount([a, b]);

        Assert.Equal("frontier.b", vfs.Find("shared.png")!.Mod);
        Assert.Equal("frontier.a", vfs.Find(new AssetId("frontier.a", "shared.png"))!.Mod);
        Assert.Equal("frontier.b", vfs.Find(new AssetId("frontier.b", "shared.png"))!.Mod);
        Assert.Contains(vfs.Issues, issue => issue.Code == "vfs.asset_conflict" && issue.Severity == ModIssueSeverity.Warning);
    }

    [Fact]
    public void Missing_roots_and_unsafe_roots_are_errors()
    {
        using var root = new ModTempFolder();
        var missing = Manifest("frontier.missing", "missing/");
        var unsafeRoot = Manifest("frontier.unsafe", "../outside/");

        var vfs = ModVfs.Mount([
            Candidate(root, "Missing", missing),
            Candidate(root, "Unsafe", unsafeRoot),
        ]);

        Assert.Contains(vfs.Issues, issue => issue.Code == "vfs.root_missing" && issue.Severity == ModIssueSeverity.Error);
        Assert.Contains(vfs.Issues, issue => issue.Code == "vfs.root_path" && issue.Severity == ModIssueSeverity.Error);
    }

    [Fact]
    public void Content_files_are_enumerated_per_mod()
    {
        using var root = new ModTempFolder();
        root.Write("A/assets/a.png", "a");
        root.Write("A/content/items/a.yaml", "id: a");
        root.Write("A/content/items/a.yml", "id: a");
        root.Write("A/content/items/readme.txt", "ignored");
        var manifest = Manifest("frontier.a", "assets/");
        manifest.Content = ["content/"];

        var vfs = ModVfs.Mount([Candidate(root, "A", manifest)]);

        Assert.Equal(2, vfs.ContentOf("frontier.a").Count());
        Assert.DoesNotContain(vfs.ContentFiles, file => file.RelativePath.EndsWith(".txt", StringComparison.Ordinal));
    }
}
