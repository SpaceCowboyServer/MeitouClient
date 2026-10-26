using Meitou.Content;
using Meitou.Modding;

namespace Meitou.Tests.Modding;

public class ModDiscoveryTests
{
    static GameInstall Install(ModTempFolder folder, string modsCfg, params (string Name, string Manifest)[] mods)
    {
        folder.Write("data/gamedata.base", "");
        folder.Write("data/mods.cfg", modsCfg);
        foreach (var (name, manifest) in mods)
        {
            folder.Write("mods/" + name + "/" + name + ".mod", "");
            folder.Write("mods/" + name + "/meitou.json", manifest);
        }
        return GameInstall.Open(folder.Path);
    }

    [Fact]
    public void Discovers_manifests_and_maps_mods_cfg_names_to_ids()
    {
        using var folder = new ModTempFolder();
        var install = Install(folder, "A.mod\r\nB.mod\r\n",
            ("A", ModFixtures.ManifestJson("frontier.a")),
            ("B", ModFixtures.ManifestJson("frontier.b")));

        var mods = ModList.Discover(install);
        var discovery = ModDiscovery.Discover(mods);
        var enabled = ModDiscovery.EnabledIds(install, mods);

        Assert.True(discovery.Success);
        Assert.Equal(["frontier.a", "frontier.b"], discovery.Candidates.Select(candidate => candidate.Id));
        Assert.Equal(["frontier.a", "frontier.b"], enabled);
        Assert.All(discovery.Candidates, candidate => Assert.Matches("^[0-9a-f]{64}$", candidate.ManifestSha256));
    }

    [Fact]
    public void Resolves_an_install_and_ignores_invalid_disabled_manifests()
    {
        using var folder = new ModTempFolder();
        var install = Install(folder, "A.mod\r\nB.mod\r\n",
            ("A", ModFixtures.ManifestJson("frontier.a")),
            ("B", ModFixtures.ManifestJson("frontier.b", extra: ", \"depends\": { \"frontier.a\": \">=1\" }")),
            ("C", ModFixtures.ManifestJson("bad")));

        var mods = ModList.Discover(install);
        var resolution = ModDiscovery.Resolve(install, mods);

        Assert.True(resolution.Success, string.Join(" | ", resolution.Errors.Select(issue => issue.Message)));
        Assert.Equal(["frontier.a", "frontier.b"], resolution.Order.Select(candidate => candidate.Id));
    }

    [Fact]
    public void Invalid_enabled_manifest_blocks_resolution()
    {
        using var folder = new ModTempFolder();
        var install = Install(folder, "A.mod\r\nC.mod\r\n",
            ("A", ModFixtures.ManifestJson("frontier.a")),
            ("C", ModFixtures.ManifestJson("bad")));

        var mods = ModList.Discover(install);
        var resolution = ModDiscovery.Resolve(install, mods);

        Assert.False(resolution.Success);
        Assert.Contains(resolution.Errors, issue => issue.Code == "manifest.invalid");
    }

    [Fact]
    public void Mods_without_a_manifest_are_not_manifest_candidates()
    {
        using var folder = new ModTempFolder();
        folder.Write("data/gamedata.base", "");
        folder.Write("data/mods.cfg", "Legacy.mod\r\n");
        folder.Write("mods/Legacy/Legacy.mod", "");
        var install = GameInstall.Open(folder.Path);

        var mods = ModList.Discover(install);
        var discovery = ModDiscovery.Discover(mods);
        var enabled = ModDiscovery.EnabledIds(install, mods);

        Assert.Empty(discovery.Candidates);
        Assert.Empty(enabled);
    }
}
