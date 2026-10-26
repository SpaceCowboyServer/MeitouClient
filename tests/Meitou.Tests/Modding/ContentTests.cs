using Meitou.Modding;
using Meitou.Modding.Assets;
using Meitou.Modding.Content;
using Meitou.Modding.Vfs;

namespace Meitou.Tests.Modding;

public class ContentTests
{
    const string ItemYaml = """
        id: revolver
        type: item
        name: Frontier Revolver
        assets:
          icon: frontier.revolver:weapons/revolver.png
          mesh: frontier.revolver:weapons/revolver.mesh
          sound: frontier.revolver:weapons/revolver_shot.wav
        data:
          value: 120
        """;

    static ModManifest Manifest(string id, string assets = "assets/", string content = "content/") => new()
    {
        Id = id,
        Version = "1.0.0",
        Assets = [assets],
        Content = [content],
    };

    static ModCandidate Candidate(ModTempFolder root, string folder, ModManifest manifest) =>
        ModFixtures.CandidateAt(folder, System.IO.Path.Combine(root.Path, folder), manifest);

    static void WriteRevolver(ModTempFolder root, string folder = "Revolver", string yaml = ItemYaml)
    {
        root.Write(folder + "/assets/weapons/revolver.png", "png");
        root.Write(folder + "/assets/weapons/revolver.mesh", "mesh");
        root.Write(folder + "/assets/weapons/revolver_shot.wav", "wav");
        root.Write(folder + "/content/items/revolver.yaml", yaml);
    }

    [Fact]
    public void Loads_an_item_and_qualifies_its_content_id()
    {
        using var root = new ModTempFolder();
        WriteRevolver(root);
        var vfs = ModVfs.Mount([Candidate(root, "Revolver", Manifest("frontier.revolver"))]);

        var result = ContentLoader.Load(vfs);

        Assert.True(result.Success, string.Join(" | ", result.Issues.Select(issue => issue.Message)));
        Assert.True(ContentId.TryParse("frontier.revolver:item/revolver", out var id));
        var entry = result.Registry.Find(id);
        Assert.NotNull(entry);
        Assert.Equal("Frontier Revolver", entry.Document.Name);
        Assert.Equal("frontier.revolver:weapons/revolver.png", entry.Document.Assets!["icon"]);
        Assert.Single(result.Registry.Items);

        var item = result.Registry.FindItem(id);
        Assert.NotNull(item);
        Assert.Equal("Frontier Revolver", item.Name);
        Assert.Equal(new AssetId("frontier.revolver", "weapons/revolver.png"), item.Icon);
        Assert.Equal(new AssetId("frontier.revolver", "weapons/revolver.mesh"), item.Mesh);
        Assert.Equal(new AssetId("frontier.revolver", "weapons/revolver_shot.wav"), item.Sound);
    }

    [Fact]
    public void Missing_referenced_asset_is_an_error()
    {
        using var root = new ModTempFolder();
        root.Write("Revolver/assets/weapons/revolver.png", "png");
        root.Write("Revolver/content/items/revolver.yaml", ItemYaml);
        var vfs = ModVfs.Mount([Candidate(root, "Revolver", Manifest("frontier.revolver"))]);

        var result = ContentLoader.Load(vfs);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Code == "content.asset_missing");
    }

    [Fact]
    public void Asset_with_the_wrong_kind_is_an_error()
    {
        using var root = new ModTempFolder();
        WriteRevolver(root, yaml: ItemYaml.Replace("weapons/revolver.png", "weapons/revolver.mesh", StringComparison.Ordinal));
        var vfs = ModVfs.Mount([Candidate(root, "Revolver", Manifest("frontier.revolver"))]);

        var result = ContentLoader.Load(vfs);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Code == "content.asset_kind");
    }

    [Fact]
    public void Missing_required_asset_role_is_an_error()
    {
        using var root = new ModTempFolder();
        root.Write("Revolver/assets/weapons/revolver.mesh", "mesh");
        root.Write("Revolver/content/items/revolver.yaml", """
            id: revolver
            type: item
            name: Frontier Revolver
            assets:
              mesh: frontier.revolver:weapons/revolver.mesh
            """);
        var vfs = ModVfs.Mount([Candidate(root, "Revolver", Manifest("frontier.revolver"))]);

        var result = ContentLoader.Load(vfs);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Code == "content.asset_required");
    }

    [Theory]
    [InlineData("type: weapon", "content.type")]
    [InlineData("schema: 2", "content.schema")]
    [InlineData("assets:\n  BadRole!: frontier.revolver:weapons/revolver.png", "content.asset_role")]
    [InlineData("assets:\n  icon: ../revolver.png", "content.asset_id")]
    public void Reports_schema_and_asset_id_problems(string replacement, string code)
    {
        using var root = new ModTempFolder();
        string yaml = replacement.StartsWith("type:", StringComparison.Ordinal)
            ? ItemYaml.Replace("type: item", replacement, StringComparison.Ordinal)
            : replacement.StartsWith("schema:", StringComparison.Ordinal)
                ? ItemYaml.Replace("type: item", "type: item\n" + replacement, StringComparison.Ordinal)
                : ItemYaml.Replace("assets:\n  icon: frontier.revolver:weapons/revolver.png", replacement, StringComparison.Ordinal);
        WriteRevolver(root, yaml: yaml);
        var vfs = ModVfs.Mount([Candidate(root, "Revolver", Manifest("frontier.revolver"))]);

        var result = ContentLoader.Load(vfs);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Code == code);
    }

    [Fact]
    public void Malformed_yaml_is_an_error()
    {
        using var root = new ModTempFolder();
        WriteRevolver(root, yaml: "id: [");
        var vfs = ModVfs.Mount([Candidate(root, "Revolver", Manifest("frontier.revolver"))]);

        var result = ContentLoader.Load(vfs);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Code == "content.yaml");
    }

    [Fact]
    public void Duplicate_yaml_keys_are_errors()
    {
        using var root = new ModTempFolder();
        WriteRevolver(root, yaml: ItemYaml.Replace("name: Frontier Revolver", "name: First\nname: Second", StringComparison.Ordinal));
        var vfs = ModVfs.Mount([Candidate(root, "Revolver", Manifest("frontier.revolver"))]);

        var result = ContentLoader.Load(vfs);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Code == "content.yaml");
    }

    [Fact]
    public void Duplicate_content_ids_are_errors()
    {
        using var root = new ModTempFolder();
        root.Write("A/assets/a.png", "a");
        root.Write("A/assets/a.mesh", "a");
        const string yaml = "id: shared\ntype: item\nname: A\nassets:\n  icon: frontier.a:a.png\n  mesh: frontier.a:a.mesh";
        root.Write("A/content/one.yaml", yaml);
        root.Write("A/content/two.yaml", yaml);
        var vfs = ModVfs.Mount([Candidate(root, "A", Manifest("frontier.a"))]);

        var result = ContentLoader.Load(vfs);

        Assert.False(result.Success);
        Assert.Contains(result.Issues, issue => issue.Code == "content.duplicate");
    }

    [Fact]
    public void A_custom_schema_can_accept_a_new_type()
    {
        using var root = new ModTempFolder();
        root.Write("Mod/content/thing.yaml", "id: thing\ntype: weapon\nname: A thing");
        var vfs = ModVfs.Mount([Candidate(root, "Mod", Manifest("frontier.mod", assets: "assets/", content: "content/"))]);
        var schemas = new ContentSchemaRegistry([new GenericContentSchema("weapon")]);

        var result = ContentLoader.Load(vfs, schemas);

        Assert.True(result.Success, string.Join(" | ", result.Issues.Select(issue => issue.Message)));
        Assert.True(ContentId.TryParse("frontier.mod:weapon/thing", out var id));
        Assert.NotNull(result.Registry.Find(id));
    }

    [Fact]
    public void Bundle_combines_manifest_vfs_and_content_checks()
    {
        using var root = new ModTempFolder();
        WriteRevolver(root);
        var candidate = Candidate(root, "Revolver", Manifest("frontier.revolver"));
        var resolution = ModResolver.Resolve([candidate], ["frontier.revolver"]);

        var bundle = ModContentBundle.Load(resolution);

        Assert.True(bundle.Success, string.Join(" | ", bundle.Errors.Select(issue => issue.Message)));
        Assert.Single(bundle.Content.Items);
        Assert.Equal(3, bundle.Vfs.Assets.Count);
        Assert.True(ContentId.TryParse("frontier.revolver:item/revolver", out var id));
        Assert.NotNull(bundle.Content.Find(id));
    }
}
