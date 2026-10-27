using Meitou.Modding;

namespace Meitou.Tests.Modding;

public class ModManifestTests
{
    static ModManifest Valid() => new() { Id = "frontier.test", Version = "1.0.0" };

    [Fact]
    public void Reads_every_supported_field()
    {
        using var folder = new ModTempFolder();
        string path = folder.Write("meitou.json", """
            {
              "id": "frontier.bounty.dust_bandit",
              "version": "1.2.3-beta.1",
              "apiVersion": 1,
              "gameVersion": ">=0.16 <0.17",
              "depends": { "frontier.bounties": ">=1.2 <2", "frontier.revolver": ">=1.0 <2" },
              "optional": { "frontier.weather": ">=0.5" },
              "provides": ["frontier.contracts@1"],
              "loadAfter": ["frontier.bounties"],
              "conflicts": ["frontier.old"],
              "permissions": ["events:bounty", "contracts:write"],
              "content": ["content/"],
              "assets": ["assets/"],
              "trust": "script",
              "entry": "scripts/main.lua"
            }
            """);

        var result = ModManifest.Read(path);

        Assert.True(result.Success, result.Error);
        var manifest = result.Manifest!;
        Assert.Equal("frontier.bounty.dust_bandit", manifest.Id);
        Assert.Equal("1.2.3-beta.1", manifest.Version);
        Assert.Equal(">=0.16 <0.17", manifest.GameVersion);
        Assert.Equal(">=1.2 <2", manifest.Depends["frontier.bounties"]);
        Assert.Equal(">=0.5", manifest.Optional["frontier.weather"]);
        Assert.Equal(["frontier.contracts@1"], manifest.Provides);
        Assert.Equal(["frontier.bounties"], manifest.LoadAfter);
        Assert.Equal(["frontier.old"], manifest.Conflicts);
        Assert.Equal(["events:bounty", "contracts:write"], manifest.Permissions);
        Assert.Equal(["content/"], manifest.Content);
        Assert.Equal(["assets/"], manifest.Assets);
        Assert.Equal(ModTrust.Script, manifest.Trust);
        Assert.Equal("scripts/main.lua", manifest.Entry);
        Assert.Matches("^[0-9a-f]{64}$", result.ManifestSha256);
    }

    [Fact]
    public void Missing_manifest_is_not_an_error()
    {
        using var folder = new ModTempFolder();
        var result = ModManifest.Read(System.IO.Path.Combine(folder.Path, "meitou.json"));

        Assert.False(result.Success);
        Assert.Null(result.Error);
        Assert.Null(result.ManifestSha256);
    }

    [Fact]
    public void Malformed_json_reports_an_error_and_still_hashes_the_file()
    {
        using var folder = new ModTempFolder();
        string path = folder.Write("meitou.json", "{");

        var result = ModManifest.Read(path);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Matches("^[0-9a-f]{64}$", result.ManifestSha256);
    }

    [Fact]
    public void Unknown_trust_is_rejected()
    {
        using var folder = new ModTempFolder();
        string path = folder.Write("meitou.json", ModFixtures.ManifestJson("frontier.test", extra: ", \"trust\": \"native\""));

        var result = ModManifest.Read(path);

        Assert.False(result.Success);
        Assert.Contains("native", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Trailing_commas_and_null_collections_are_accepted()
    {
        using var folder = new ModTempFolder();
        string path = folder.Write("meitou.json", """
            {
              "id": "frontier.test",
              "version": "1.0.0",
              "depends": null,
              "optional": null,
              "provides": null,
              "trust": "data",
            }
            """);

        var result = ModManifest.Read(path);

        Assert.True(result.Success, result.Error);
        Assert.Empty(result.Manifest!.Depends);
        Assert.Empty(result.Manifest.Optional);
        Assert.Empty(result.Manifest.Provides);
    }

    [Fact]
    public void Duplicate_capabilities_and_traversal_paths_are_rejected()
    {
        using var folder = new ModTempFolder();

        var duplicate = ModManifest.Read(folder.Write("duplicate.json",
            ModFixtures.ManifestJson("frontier.test", extra: ", \"provides\": [\"frontier.contracts@1\", \"frontier.contracts@2\"]")));
        Assert.False(duplicate.Success);

        var traversal = ModManifest.Read(folder.Write("traversal.json",
            ModFixtures.ManifestJson("frontier.test", extra: ", \"content\": [\"../secret\"]")));
        Assert.False(traversal.Success);

        var permissions = ModManifest.Read(folder.Write("permissions.json",
            ModFixtures.ManifestJson("frontier.test", extra: ", \"permissions\": [\"events:everything\"]")));
        Assert.False(permissions.Success);
        Assert.Contains("events:everything", permissions.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Validation_accepts_valid_data_script_and_plugin_manifests()
    {
        Assert.Null(ModManifestValidation.Validate(Valid()));

        var script = Valid();
        script.Trust = ModTrust.Script;
        script.Entry = "scripts/main.lua";
        Assert.Null(ModManifestValidation.Validate(script));

        var plugin = Valid();
        plugin.Trust = ModTrust.Plugin;
        plugin.Assemblies.Add("lib/test.dll");
        plugin.Entry = "Test.Mod";
        Assert.Null(ModManifestValidation.Validate(plugin));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("api")]
    [InlineData("gameVersion")]
    [InlineData("dependencyId")]
    [InlineData("dependencyRange")]
    [InlineData("emptyDependencyRange")]
    [InlineData("provide")]
    [InlineData("loadAfter")]
    [InlineData("conflicts")]
    [InlineData("contentPath")]
    [InlineData("assetPath")]
    [InlineData("assemblyPath")]
    [InlineData("assemblyExtension")]
    [InlineData("dataAssemblies")]
    [InlineData("pluginWithoutAssemblies")]
    [InlineData("pluginWithoutEntry")]
    [InlineData("scriptWithoutEntry")]
    [InlineData("scriptEntryPath")]
    [InlineData("scriptAssemblies")]
    public void Validation_rejects_invalid_manifests(string mutation)
    {
        var manifest = Valid();
        switch (mutation)
        {
            case "id": manifest.Id = "frontier"; break;
            case "version": manifest.Version = "1.2"; break;
            case "api": manifest.ApiVersion = 0; break;
            case "gameVersion": manifest.GameVersion = ">=later"; break;
            case "dependencyId": manifest.Depends["frontier"] = ">=1"; break;
            case "dependencyRange": manifest.Depends["frontier.other"] = "^1"; break;
            case "emptyDependencyRange": manifest.Depends["frontier.other"] = ""; break;
            case "provide": manifest.Provides.Add("frontier"); break;
            case "loadAfter": manifest.LoadAfter.Add("frontier"); break;
            case "conflicts": manifest.Conflicts.Add("frontier"); break;
            case "contentPath": manifest.Content.Add("../content"); break;
            case "assetPath": manifest.Assets.Add("/assets"); break;
            case "assemblyPath": manifest.Assemblies.Add("../test.dll"); break;
            case "assemblyExtension": manifest.Assemblies.Add("lib/test.exe"); break;
            case "dataAssemblies": manifest.Assemblies.Add("lib/test.dll"); break;
            case "pluginWithoutAssemblies": manifest.Trust = ModTrust.Plugin; manifest.Entry = "Test.Mod"; break;
            case "pluginWithoutEntry": manifest.Trust = ModTrust.Plugin; manifest.Assemblies.Add("lib/test.dll"); break;
            case "scriptWithoutEntry": manifest.Trust = ModTrust.Script; break;
            case "scriptEntryPath": manifest.Trust = ModTrust.Script; manifest.Entry = "../main.lua"; break;
            case "scriptAssemblies":
                manifest.Trust = ModTrust.Script;
                manifest.Entry = "scripts/main.lua";
                manifest.Assemblies.Add("lib/test.dll");
                break;
        }

        Assert.NotNull(ModManifestValidation.Validate(manifest));
    }

    [Fact]
    public void Validation_normalizes_null_collections()
    {
        var manifest = Valid();
        manifest.Depends = null!;
        manifest.Optional = null!;
        manifest.Provides = null!;
        manifest.LoadAfter = null!;
        manifest.Conflicts = null!;
        manifest.Content = null!;
        manifest.Assets = null!;
        manifest.Assemblies = null!;

        Assert.Null(ModManifestValidation.Validate(manifest));
        Assert.NotNull(manifest.Depends);
        Assert.NotNull(manifest.Optional);
        Assert.NotNull(manifest.Provides);
    }
}
