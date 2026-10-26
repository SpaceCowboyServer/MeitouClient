using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Game;
using Meitou.Modding;

namespace Meitou.Tests.Modding;

public class ModLifecycleTests
{
    [Fact]
    public void Save_camera_round_trips_the_meitou_mod_set()
    {
        var camera = new SaveCamera(new FcsRecord());
        var mods = new[]
        {
            new ModSnapshot("frontier.revolver", "1.2.0", "Revolver", ModTrust.Data),
            new ModSnapshot("frontier.bounties", "1.3.0", "Bounties", ModTrust.Plugin),
            new ModSnapshot("", "", "Legacy Mod", ModTrust.Data),
        };

        camera.SetMeitouMods(mods);
        Assert.Equal(mods, camera.MeitouMods);

        camera.SetMeitouMods([mods[0]]);
        Assert.Equal(new[] { mods[0] }, camera.MeitouMods);
        Assert.False(camera.Record.Strings.ContainsKey("meitou mod1 id"));
        Assert.Equal(1, camera.Record.Ints["meitou mod count"]);
    }

    [Fact]
    public void Save_file_round_trips_the_meitou_mod_set()
    {
        using var folder = new ModTempFolder();
        var builder = new SaveBuilder(3);
        var mods = new[]
        {
            new ModSnapshot("frontier.dust", "2.1.0", "Dust", ModTrust.Script),
            new ModSnapshot("", "", "Legacy", ModTrust.Data),
        };
        builder.Game.Camera.SetMeitouMods(mods);
        builder.Game.UpdateDerived();
        builder.Game.Write(folder.Path);

        var loaded = SaveGame.Load(folder.Path);

        Assert.Equal(mods, loaded.Camera.MeitouMods);
    }

    [Fact]
    public void Mod_set_comparison_reports_missing_versions_trust_and_new_mods()
    {
        var saved = new[]
        {
            new ModSnapshot("frontier.old", "1.0.0", "Old", ModTrust.Plugin),
            new ModSnapshot("frontier.gone", "2.0.0", "Gone", ModTrust.Script),
            new ModSnapshot("", "", "Legacy", ModTrust.Data),
        };
        var current = new[]
        {
            new ModSnapshot("frontier.old", "1.1.0", "Old", ModTrust.Plugin),
            new ModSnapshot("frontier.new", "3.0.0", "New", ModTrust.Data),
            new ModSnapshot("", "", "Legacy", ModTrust.Data),
        };

        var diff = ModSnapshots.Compare(saved, current);

        Assert.Contains(diff.Issues, line => line.Contains("Missing mod 'frontier.gone'", StringComparison.Ordinal));
        Assert.Contains(diff.Issues, line => line.Contains("'frontier.old' is 1.1.0 now", StringComparison.Ordinal));
        Assert.Contains(diff.Notes, line => line.Contains("New since the save: frontier.new 3.0.0", StringComparison.Ordinal));
        Assert.DoesNotContain(diff.Issues, line => line.Contains("Legacy", StringComparison.Ordinal));

        var trust = ModSnapshots.Compare([saved[0]], [new ModSnapshot("frontier.old", "1.0.0", "Old", ModTrust.Script)]);
        Assert.Contains(trust.Issues, line => line.Contains("is now script", StringComparison.Ordinal) && line.Contains("made with plugin", StringComparison.Ordinal));
    }

    [Fact]
    public void Game_options_parse_safe_mode_and_validate_mods()
    {
        var (options, rest) = GameOptions.Split(["--safe-mode", "--validate-mods", "--new-game"]);

        Assert.True(options.SafeMode);
        Assert.True(options.ValidateMods);
        Assert.True(options.NewGame);
        Assert.Empty(rest);
    }

    [Fact]
    public void Code_diagnostics_find_missing_scripts_and_assemblies()
    {
        using var root = new ModTempFolder();
        string scriptFolder = Path.Combine(root.Path, "MissingScript");
        Directory.CreateDirectory(scriptFolder);
        var script = ModFixtures.CandidateAt("MissingScript", scriptFolder, ModFixtures.Script("frontier.script"));

        string pluginFolder = Path.Combine(root.Path, "MissingPlugin");
        Directory.CreateDirectory(pluginFolder);
        var plugin = ModFixtures.CandidateAt("MissingPlugin", pluginFolder, ModFixtures.Plugin("frontier.plugin"));

        string goodFolder = Path.Combine(root.Path, "Good");
        Directory.CreateDirectory(Path.Combine(goodFolder, "scripts"));
        File.WriteAllText(Path.Combine(goodFolder, "scripts", "main.lua"), "meitou.log.info('ok')");
        var good = ModFixtures.CandidateAt("Good", goodFolder, ModFixtures.Script("frontier.good"));

        var issues = ModFileDiagnostics.Validate(new ModResolution([script, plugin, good], [], ModApi.CurrentVersion, null));

        Assert.Contains(issues, issue => issue.Code == "mod.script_missing");
        Assert.Contains(issues, issue => issue.Code == "mod.assembly_missing");
        Assert.DoesNotContain(issues, issue => issue.ModId == "frontier.good");
    }

    [Fact]
    public void Safe_mode_load_order_skips_mods_cfg_without_changing_the_global_flag()
    {
        using var root = new ModTempFolder();
        root.Write("data/gamedata.base", "");
        root.Write("data/mods.cfg", "A.mod\r\n");
        root.Write("mods/A/A.mod", "");
        var install = Meitou.Content.GameInstall.Open(root.Path);

        var normal = Meitou.Data.LoadOrder.FromInstall(install, safeMode: false);
        var safe = Meitou.Data.LoadOrder.FromInstall(install, safeMode: true);

        Assert.Contains(normal.Entries, entry => entry.Name == "A.mod");
        Assert.DoesNotContain(safe.Entries, entry => entry.Name == "A.mod");
        Assert.All(safe.Entries, entry => Assert.Contains(entry.Name, Meitou.Data.LoadOrder.BaseFiles));
        Assert.False(Meitou.Data.LoadOrder.SafeMode);
    }

    [Fact]
    public void Current_snapshots_keep_manifest_order_and_add_enabled_legacy_mods()
    {
        using var root = new ModTempFolder();
        root.Write("data/gamedata.base", "");
        root.Write("data/mods.cfg", "A.mod\r\nLegacy.mod\r\n");
        root.Write("mods/A/A.mod", "");
        root.Write("mods/A/meitou.json", ModFixtures.ManifestJson("frontier.a"));
        root.Write("mods/Legacy/Legacy.mod", "");
        var install = Meitou.Content.GameInstall.Open(root.Path);
        var resolution = ModDiscovery.Resolve(install);

        var snapshots = ModSnapshots.Current(install, resolution);

        Assert.Equal(["frontier.a", "Legacy"], snapshots.Select(snapshot => snapshot.Name));
        Assert.Equal(ModTrust.Data, snapshots[1].Trust);
        Assert.Equal("", snapshots[1].Id);
    }
}
