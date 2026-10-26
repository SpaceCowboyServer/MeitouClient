using System.Numerics;
using Meitou.Data;
using Meitou.Modding;
using Meitou.Modding.Scripting;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Bounties;
using Meitou.Tests.Simulation;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Modding;

public class LuaModHostTests
{
    static (LuaModHost Host, List<string> Log) Load(ModTempFolder root, params (string Id, string Script)[] mods)
    {
        var candidates = new List<ModCandidate>();
        foreach (var (id, script) in mods)
        {
            string folder = Path.Combine(root.Path, id);
            Directory.CreateDirectory(folder);
            root.Write(id + "/scripts/main.lua", script);
            var manifest = ModFixtures.Script(id);
            candidates.Add(ModFixtures.CandidateAt(id, folder, manifest));
        }
        var log = new List<string>();
        var resolution = new ModResolution(candidates, [], ModApi.CurrentVersion, null);
        return (LuaModHost.Load(resolution, SyntheticTown.Database(), log.Add), log);
    }

    [Fact]
    public void Loads_a_sandboxed_script_and_registers_a_faction_contract()
    {
        using var root = new ModTempFolder();
        var (host, log) = Load(root, ("frontier.dust", """
            assert(io == nil and os == nil and load == nil and dofile == nil and require == nil and debug == nil, "sandbox leak")
            local id = meitou.contracts.register {
              id = "dust",
              kind = "faction",
              faction = "Testfolk",
              title = "Dust Contract",
              required = 1,
              reward = 777,
            }
            meitou.log.info("registered " .. id)
            """));

        Assert.Equal(1, host.ScriptCount);
        Assert.Equal(["frontier.dust"], host.LoadedModIds);
        var definition = Assert.Single(host.Contracts.Definitions);
        Assert.Equal("frontier.dust:bounty/dust", definition.Id);
        Assert.Equal(BountyTargetKind.Faction, definition.Kind);
        Assert.Equal("20-t", definition.FactionId);
        Assert.Equal(777, definition.Reward);
        Assert.Contains(log, line => line.Contains("registered frontier.dust:bounty/dust", StringComparison.Ordinal));
    }

    [Fact]
    public void A_script_contract_is_offered_accepted_paid_and_reports_events()
    {
        using var root = new ModTempFolder();
        var (host, log) = Load(root, ("frontier.dust", """
            meitou.contracts.register {
              id = "dust",
              kind = "faction",
              faction = "20-t",
              title = "Dust Contract",
              required = 1,
              reward = 777,
            }
            meitou.events.on("bounty.accepted", function(b)
              meitou.log.info("accepted " .. b.id)
            end)
            meitou.events.on("bounty.completed", function(b)
              meitou.log.info("completed " .. b.title .. " " .. tostring(b.reward))
            end)
            """));

        var data = SyntheticTown.Data(SyntheticTown.Database());
        var board = new BountyBoardSystem(data, new BountySettings
        {
            Contracts = host.Contracts,
            OfferRefreshTicks = 1,
            ViewRefreshTicks = 1,
            OfferSpecific = false,
            OfferAnimals = false,
            OfferSpawned = false,
        });
        var set = new StandardSystemSet { Systems = new List<ITickSystem> { board } };
        host.AddSystems(set);
        using var world = new SimWorld(new WorldSettings { Seed = 1, Threads = 1, PublishSnapshots = false },
            new OpenGroundWalkability((_, _) => 0), set.Systems);
        var player = Spawn(world, data.PlayerFaction, true, "hero", Vector2.Zero);
        var target = Spawn(world, data.FactionIndex("20-t"), false, "target", new Vector2(100, 0));
        world.RunTicks(1);

        var offer = Assert.Single(board.Published.Offers);
        Assert.Equal("frontier.dust:bounty/dust", offer.Id);
        Assert.Equal("Dust Contract", offer.Title);
        world.Commands.Enqueue(new AcceptBountyCommand(offer.Id) { Tick = world.Tick });
        world.RunTicks(1);

        var contract = Assert.Single(board.Contracts);
        Assert.Equal(BountyState.Accepted, contract.State);
        board.Died(world, player.Slot, target.Slot);
        Assert.Equal(777, world.Player.Money);
        Assert.Equal(BountyState.Completed, contract.State);
        world.RunTicks(1);

        Assert.Contains(log, line => line.Contains("accepted frontier.dust:bounty/dust", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("completed Dust Contract 777", StringComparison.Ordinal));
    }

    [Fact]
    public void A_broken_script_is_isolated_and_its_contracts_are_removed()
    {
        using var root = new ModTempFolder();
        var (host, log) = Load(root,
            ("frontier.good", """
                meitou.contracts.register { id = "good", kind = "faction", faction = "20-t", title = "Good Contract" }
                """),
            ("frontier.broken", """
                meitou.contracts.register { id = "broken", kind = "faction", faction = "20-t", title = "Broken Contract" }
                error("broken on purpose")
                """));

        Assert.Equal(1, host.ScriptCount);
        Assert.Equal(["frontier.good"], host.LoadedModIds);
        var definition = Assert.Single(host.Contracts.Definitions);
        Assert.Equal("frontier.good:bounty/good", definition.Id);
        Assert.Contains(log, line => line.Contains("broken on purpose", StringComparison.Ordinal));
    }

    [Fact]
    public void A_throwing_event_disables_only_that_script()
    {
        using var root = new ModTempFolder();
        var (host, log) = Load(root,
            ("frontier.good", """
                meitou.events.on("world.tick", function(w) meitou.log.info("tick " .. tostring(w.tick)) end)
                """),
            ("frontier.bad", """
                meitou.events.on("world.tick", function() error("event boom") end)
                """));

        var set = new StandardSystemSet { Systems = new List<ITickSystem>() };
        host.AddSystems(set);
        using var world = new SimWorld(new WorldSettings { Seed = 1, Threads = 1, PublishSnapshots = false },
            new OpenGroundWalkability((_, _) => 0), set.Systems);
        world.RunTicks(2);

        Assert.Equal(2, log.Count(line => line.Contains("tick", StringComparison.Ordinal) && line.Contains("frontier.good", StringComparison.Ordinal)));
        Assert.Contains(log, line => line.Contains("tick 1", StringComparison.Ordinal));
        Assert.Contains(log, line => line.Contains("disabled", StringComparison.Ordinal) && line.Contains("event boom", StringComparison.Ordinal));
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
