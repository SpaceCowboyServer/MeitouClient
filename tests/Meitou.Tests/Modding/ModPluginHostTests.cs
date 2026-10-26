using Meitou.Data;
using Meitou.Modding;
using Meitou.Modding.Api;
using Meitou.Modding.Plugins;
using Meitou.Simulation;

namespace Meitou.Tests.Modding;

public class ModPluginHostTests
{
    static ModCandidate Plugin(
        ModTempFolder root,
        string folder,
        string id,
        string entry,
        bool copyAssembly = true,
        ModTrust trust = ModTrust.Plugin,
        int apiVersion = ModApi.CurrentVersion)
    {
        string directory = System.IO.Path.Combine(root.Path, folder);
        Directory.CreateDirectory(directory);
        if (copyAssembly)
            File.Copy(typeof(TestMod).Assembly.Location, System.IO.Path.Combine(directory, "Meitou.Tests.dll"), overwrite: true);

        var manifest = ModFixtures.Manifest(id);
        manifest.Trust = trust;
        manifest.Assemblies.Add(copyAssembly ? "Meitou.Tests.dll" : "Missing.dll");
        manifest.Entry = entry;
        manifest.ApiVersion = apiVersion;
        return ModFixtures.CandidateAt(folder, directory, manifest);
    }

    static (ModPluginHost Host, List<string> Log) Load(params ModCandidate[] candidates)
    {
        var log = new List<string>();
        var host = ModPluginHost.Load(new ModResolution(candidates, [], ModApi.CurrentVersion, null), new GameDatabase(), log.Add);
        return (host, log);
    }

    static int[] State(ModPluginHost host)
    {
        Assert.True(host.Services.TryGet("test", out var service));
        return Assert.IsType<int[]>(service);
    }

    [Fact]
    public void Loads_a_plugin_and_registers_service_system_saveable_and_command()
    {
        using var root = new ModTempFolder();
        var (host, log) = Load(Plugin(root, "Test", "frontier.test", "Meitou.Tests.Modding.TestMod"));

        Assert.Equal(1, host.PluginCount);
        Assert.Equal(1, host.SystemCount);
        Assert.Equal(1, host.SaveableCount);
        Assert.Equal(["frontier.test"], host.LoadedModIds);
        Assert.Equal(7, State(host)[0]);
        Assert.Contains(log, line => line.Contains("initialized", StringComparison.Ordinal));

        var set = new StandardSystemSet { Systems = new List<ITickSystem>() };
        host.AddSystems(set);
        Assert.Single(set.Systems);

        using var world = new Meitou.Simulation.World(new WorldSettings { Threads = 1, PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0));
        host.AttachWorld(world);
        Assert.Equal(1, world.Commands.Count);
    }

    [Fact]
    public void Sidecar_round_trips_a_plugins_saveable()
    {
        using var root = new ModTempFolder();
        var (host, _) = Load(Plugin(root, "Test", "frontier.test", "Meitou.Tests.Modding.TestMod"));
        var state = State(host);

        var sidecars = host.CaptureSidecars();
        Assert.True(sidecars.ContainsKey("frontier.test"));

        using var save = new ModTempFolder();
        host.WriteSidecars(save.Path, sidecars);
        Assert.True(File.Exists(System.IO.Path.Combine(save.Path, "meitou-mods", "frontier.test.bin")));

        state[0] = 0;
        host.ReadSidecars(save.Path);
        Assert.Equal(7, state[0]);

        host.ReadSidecars(System.IO.Path.Combine(root.Path, "missing-save"));
    }

    [Fact]
    public void A_broken_plugin_is_isolated_and_the_other_loads()
    {
        using var root = new ModTempFolder();
        var good = Plugin(root, "Good", "frontier.good", "Meitou.Tests.Modding.TestMod");
        var broken = Plugin(root, "Broken", "frontier.broken", "Meitou.Tests.Modding.BrokenMod");

        var (host, log) = Load(good, broken);

        Assert.Equal(1, host.PluginCount);
        Assert.Equal(["frontier.good"], host.LoadedModIds);
        Assert.Contains(log, line => line.Contains("broken on purpose", StringComparison.Ordinal));
    }

    [Fact]
    public void A_service_from_a_failed_initialization_is_rolled_back()
    {
        using var root = new ModTempFolder();
        var candidate = Plugin(root, "Failing", "frontier.failing", "Meitou.Tests.Modding.FailingAfterServiceMod");

        var (host, log) = Load(candidate);

        Assert.Equal(0, host.PluginCount);
        Assert.False(host.Services.TryGet("rolled-back", out _));
        Assert.Contains(log, line => line.Contains("fails after a service", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_assembly_api_mismatch_and_non_plugin_are_reported_without_loading()
    {
        using var root = new ModTempFolder();
        var missing = Plugin(root, "Missing", "frontier.missing", "Meitou.Tests.Modding.TestMod", copyAssembly: false);
        var api = Plugin(root, "Api", "frontier.api", "Meitou.Tests.Modding.TestMod", apiVersion: ModApi.CurrentVersion + 1);
        var data = Plugin(root, "Data", "frontier.data", "Meitou.Tests.Modding.TestMod", trust: ModTrust.Data);

        var (host, log) = Load(missing, api, data);

        Assert.Equal(0, host.PluginCount);
        Assert.Contains(log, line => line.Contains("missing assembly", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("needs API", StringComparison.Ordinal));
    }
}
