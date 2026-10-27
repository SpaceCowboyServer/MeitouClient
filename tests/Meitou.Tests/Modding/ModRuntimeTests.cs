using System.Numerics;
using Meitou.Data;
using Meitou.Modding;
using Meitou.Modding.Api;
using Meitou.Modding.Plugins;
using Meitou.Modding.Runtime;
using Meitou.Modding.Scripting;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Bounties;
using Meitou.Tests.Simulation;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

public class ModRuntimeTests
{
    [Fact]
    public void Csharp_and_lua_share_one_board_and_receive_the_same_events()
    {
        using var root = new ModTempFolder();
        string pluginFolder = Path.Combine(root.Path, "Provider");
        Directory.CreateDirectory(pluginFolder);
        File.Copy(typeof(BountyProviderMod).Assembly.Location, Path.Combine(pluginFolder, "Meitou.Tests.dll"));
        var pluginManifest = ModFixtures.Manifest("frontier.provider");
        pluginManifest.Trust = ModTrust.Plugin;
        pluginManifest.Assemblies.Add("Meitou.Tests.dll");
        pluginManifest.Entry = "Meitou.Tests.Modding.BountyProviderMod";
        var plugin = ModFixtures.CandidateAt("Provider", pluginFolder, pluginManifest);

        string scriptFolder = Path.Combine(root.Path, "Script");
        Directory.CreateDirectory(Path.Combine(scriptFolder, "scripts"));
        File.WriteAllText(Path.Combine(scriptFolder, "scripts", "main.lua"), """
            meitou.contracts.register { id = "lua", kind = "faction", faction = "20-t", title = "Lua contract", required = 1, reward = 2 }
            meitou.events.on("bounty.accepted", function(b) meitou.log.info("accepted " .. b.id) end)
            meitou.events.on("bounty.completed", function(b) meitou.log.info("completed " .. b.id) end)
            """);
        var script = ModFixtures.CandidateAt("Script", scriptFolder, ModFixtures.Script("frontier.script"));

        var log = new List<string>();
        var api = new ModApiRuntime(log.Add);
        var resolution = new ModResolution([plugin, script], [], ModApi.CurrentVersion, null);
        var plugins = ModPluginHost.Load(resolution, new GameDatabase(), api, log.Add);
        _ = LuaModHost.Load(resolution, SyntheticTown.Database(), api, log.Add);

        Assert.Equal(2, api.Contracts.Definitions.Count);
        Assert.Contains(api.Contracts.Definitions, definition => definition.Id == "frontier.provider:bounty/plugin");
        Assert.Contains(api.Contracts.Definitions, definition => definition.Id == "frontier.script:bounty/lua");
        Assert.True(plugins.Services.TryGet("events", out var service));
        var pluginEvents = Assert.IsType<List<string>>(service);

        var data = SyntheticTown.Data(SyntheticTown.Database());
        var board = new BountyBoardSystem(data, new BountySettings
        {
            Contracts = api.Contracts,
            OfferRefreshTicks = 1,
            ViewRefreshTicks = 1,
            OfferSpecific = false,
            OfferAnimals = false,
            OfferSpawned = false,
        });
        var systems = new List<ITickSystem> { board, new ModRuntimeSystem(api, board) };
        using var world = new SimWorld(new WorldSettings { Seed = 7, Threads = 1, PublishSnapshots = false },
            new OpenGroundWalkability((_, _) => 0), systems);

        var acceptedEvents = new List<BountyAccepted>();
        var completedEvents = new List<BountyCompleted>();
        var starts = new List<WorldStarted>();
        var ticks = new List<WorldTick>();
        api.Events.Subscribe<BountyAccepted>(acceptedEvents.Add);
        api.Events.Subscribe<BountyCompleted>(completedEvents.Add);
        api.Events.Subscribe<WorldStarted>(starts.Add);
        api.Events.Subscribe<WorldTick>(ticks.Add);

        var player = Spawn(world, data.PlayerFaction, true, "hero", Vector2.Zero);
        var first = Spawn(world, data.FactionIndex("20-t"), false, "first", new Vector2(100, 0));
        var second = Spawn(world, data.FactionIndex("20-t"), false, "second", new Vector2(100, 10));
        world.RunTicks(1);

        Assert.Equal(2, board.Published.Offers.Count);
        Assert.Equal(2, api.Board.Offers.Count);
        Assert.Contains(api.Board.Offers, offer => offer.Id == "frontier.script:bounty/lua" && offer.Title == "Lua contract");
        foreach (var offer in board.Published.Offers)
            world.Commands.Enqueue(new AcceptBountyCommand(offer.Id) { Tick = world.Tick });
        world.RunTicks(1);

        Assert.Equal(2, board.Contracts.Count);
        Assert.Equal(2, api.Board.Active.Count);
        Assert.Equal(2, acceptedEvents.Count);
        Assert.Contains(acceptedEvents, bounty => bounty.Id == "frontier.script:bounty/lua" && bounty.Title == "Lua contract" &&
            bounty.Kind == "faction" && bounty.Objective == "kill" && bounty.Required == 1 && bounty.Reward == 2 && bounty.Progress == 0);
        Assert.Contains("accepted:frontier.provider:bounty/plugin", pluginEvents);
        Assert.Contains("accepted:frontier.script:bounty/lua", pluginEvents);
        Assert.Contains(log, line => line.Contains("accepted frontier.script:bounty/lua", StringComparison.Ordinal));

        board.Died(world, player.Slot, first.Slot);
        board.Died(world, player.Slot, second.Slot);
        world.RunTicks(1);

        Assert.Equal(2, completedEvents.Count);
        Assert.Contains(completedEvents, bounty => bounty.Id == "frontier.script:bounty/lua" && bounty.Required == 1 && bounty.Reward == 2);
        Assert.Contains("completed:frontier.provider:bounty/plugin", pluginEvents);
        Assert.Contains("completed:frontier.script:bounty/lua", pluginEvents);
        Assert.Contains(log, line => line.Contains("completed frontier.script:bounty/lua", StringComparison.Ordinal));
        Assert.Empty(api.Board.Active);
        Assert.Equal(3, world.Player.Money);

        Assert.Single(starts);
        Assert.Equal(world.Seed, starts[0].Seed);
        Assert.Equal(3, ticks.Count);
        Assert.Equal(world.Tick - 1, ticks[^1].Tick);   // the tick being run; World.Tick advances after SlowWorld
    }

    static CharacterId Spawn(SimWorld world, int faction, bool player, string name, Vector2 at)
    {
        var cold = new CharacterCold
        {
            Name = name,
            Faction = faction,
            IsPlayer = player,
            Medical = new MedicalState([]),
        };
        return world.Characters.Spawn(new CharacterHot { Position = new Vector3(at.X, 0, at.Y), Health = 100 }, cold, 0);
    }
}
