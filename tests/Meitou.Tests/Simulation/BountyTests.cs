using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Bounties;
using Meitou.Simulation.Combat;
using Meitou.Simulation.Saving;
using SimWorld = Meitou.Simulation.World;

namespace Meitou.Tests.Simulation;

/// <summary>The faction bounty loop (docs/simulation.md "Crimes and bounties"): offers, acceptance, player kills, pay and the save round-trip.</summary>
public class BountyTests
{
    const int PlayerFaction = 0, Bandits = 1;

    static PopulationData Data()
    {
        var factions = new List<FactionData>
        {
            new() { Id = "player", Name = "Player", FundamentalType = FundamentalType.Civilian },
            new() { Id = "bandits", Name = "Bandits", FundamentalType = FundamentalType.Bandit },
        };
        var relations = FactionRelations.Build(factions);
        relations.PlayerFaction = PlayerFaction;
        return new PopulationData { Db = new GameDatabase(), Sites = [], Factions = factions, PlayerFaction = PlayerFaction, Relations = relations };
    }

    sealed class Rig : IDisposable
    {
        public PopulationData Data { get; }
        public BountyBoardSystem Board { get; }
        public SimWorld World { get; }
        readonly List<CharacterId> bandits = [];

        public Rig(BountySettings? settings = null)
        {
            Data = BountyTests.Data();
            Board = new BountyBoardSystem(Data, settings ?? new BountySettings { OfferRefreshTicks = 1, ViewRefreshTicks = 1 });
            var walk = new OpenGroundWalkability((_, _) => 0);
            World = new SimWorld(new WorldSettings { Seed = 1, Threads = 1, PublishSnapshots = false }, walk, [Board]);
        }

        public CharacterId Spawn(int faction, bool player, Vector2 at, string name, bool fighter = false, bool animal = false, string record = "")
        {
            var cold = new CharacterCold
            {
                Name = name, Faction = faction, IsPlayer = player, RecordId = record, Medical = new MedicalState([]),
                Fighter = fighter ? new Fighter(null) { IsAnimal = animal } : null,
            };
            var id = World.Characters.Spawn(new CharacterHot { Position = new Vector3(at.X, 0, at.Y), Health = 100 }, cold, 0);
            if (faction == Bandits) bandits.Add(id);
            return id;
        }

        public CharacterId Player(Vector2 at) => Spawn(PlayerFaction, true, at, "hero");
        public CharacterId Bandit(Vector2 at, string name = "bandit") => Spawn(Bandits, false, at, name);
        public CharacterId NamedBandit(Vector2 at, string name = "Razar") => Spawn(Bandits, false, at, name, fighter: true);
        public CharacterId Animal(Vector2 at, string record = "beak", string name = "Beak Thing") => Spawn(Bandits, false, at, name, fighter: true, animal: true, record: record);
        public CharacterCold Cold(CharacterId id) => World.Characters.Cold(id.Slot)!;
        public void Run(int ticks) => World.RunTicks(ticks);

        public void Dispose() => World.Dispose();
    }

    [Fact]
    public void Offers_a_nearby_bandit_faction_and_accept_abandon_round_trip()
    {
        using var rig = new Rig();
        rig.Player(Vector2.Zero);
        rig.Bandit(new Vector2(100, 0));
        rig.Run(1);

        var offer = Assert.Single(rig.Board.Published.Offers);
        Assert.Equal("bandits", offer.Id);
        Assert.Equal(3, offer.Required);
        Assert.Equal(4500, offer.Reward);
        Assert.Equal(100, offer.Distance, 1);

        rig.World.Commands.Enqueue(new AcceptBountyCommand("bandits") { Tick = rig.World.Tick });
        rig.Run(1);
        Assert.Empty(rig.Board.Published.Offers);
        var active = Assert.Single(rig.Board.Published.Active);
        Assert.Equal(0, active.Progress);
        Assert.Equal(3, active.Required);

        rig.World.Commands.Enqueue(new AbandonBountyCommand("bandits") { Tick = rig.World.Tick });
        rig.Run(2);
        Assert.Empty(rig.Board.Published.Active);
        Assert.Single(rig.Board.Published.Offers);
    }

    [Fact]
    public void Player_kills_count_and_the_last_one_pays_only_once()
    {
        using var rig = new Rig();
        var player = rig.Player(Vector2.Zero);
        var first = rig.Bandit(new Vector2(100, 0));
        var second = rig.Bandit(new Vector2(100, 10));
        var third = rig.Bandit(new Vector2(100, 20));
        var bystander = rig.Bandit(new Vector2(200, 0), "bystander");
        rig.Run(1);
        rig.World.Commands.Enqueue(new AcceptBountyCommand("bandits") { Tick = rig.World.Tick });
        rig.Run(1);

        rig.Board.Died(rig.World, bystander.Slot, first.Slot);   // not a player's blow: no progress
        Assert.Equal(0, rig.Board.Contracts[0].Progress);

        rig.Board.Died(rig.World, player.Slot, first.Slot);
        rig.Board.Died(rig.World, player.Slot, second.Slot);
        Assert.Equal(2, rig.Board.Contracts[0].Progress);
        Assert.Equal(0, rig.World.Player.Money);
        Assert.Equal(BountyState.Accepted, rig.Board.Contracts[0].State);

        rig.Board.Died(rig.World, player.Slot, third.Slot);
        Assert.Equal(3, rig.Board.Contracts[0].Progress);
        Assert.Equal(4500, rig.World.Player.Money);
        Assert.Equal(BountyState.Completed, rig.Board.Contracts[0].State);
        Assert.Empty(rig.Board.Published.Active);
        Assert.Contains(rig.World.TakeMessages(), message => message.Contains("Bounty complete: Bandits", StringComparison.Ordinal));

        rig.Board.Died(rig.World, player.Slot, bystander.Slot);   // a completed contract pays nothing more
        Assert.Equal(4500, rig.World.Player.Money);
    }

    [Fact]
    public void The_save_restores_active_and_completed_contracts_and_completed_ones_are_not_offered_again()
    {
        using var rig = new Rig();
        var player = rig.Player(Vector2.Zero);
        var first = rig.Bandit(new Vector2(100, 0));
        var second = rig.Bandit(new Vector2(100, 10));
        var third = rig.Bandit(new Vector2(100, 20));
        rig.Run(1);
        rig.World.Commands.Enqueue(new AcceptBountyCommand("bandits") { Tick = rig.World.Tick });
        rig.Run(1);
        rig.Board.Died(rig.World, player.Slot, first.Slot);
        Assert.Equal(1, rig.Board.Contracts[0].Progress);

        var builder = new SaveBuilder();
        BountyBoardSave.Write(builder.Game.Camera, rig.World, rig.Data);

        using var restored = new Rig();
        BountyBoardSave.Read(builder.Game.Camera, restored.World, restored.Data);
        var contract = Assert.Single(restored.Board.Contracts);
        Assert.Equal(BountyState.Accepted, contract.State);
        Assert.Equal(1, contract.Progress);

        // A completed faction stays completed across the save: its offer does not come back.
        rig.Board.Died(rig.World, player.Slot, second.Slot);
        rig.Board.Died(rig.World, player.Slot, third.Slot);
        BountyBoardSave.Write(builder.Game.Camera, rig.World, rig.Data);
        using var completed = new Rig();
        completed.Player(Vector2.Zero);
        completed.Bandit(new Vector2(100, 0));
        BountyBoardSave.Read(builder.Game.Camera, completed.World, completed.Data);
        completed.Run(2);
        Assert.Equal(BountyState.Completed, Assert.Single(completed.Board.Contracts).State);
        Assert.Empty(completed.Board.Published.Offers);
    }

    [Fact]
    public void A_saved_contract_for_a_faction_the_load_order_dropped_is_skipped()
    {
        using var rig = new Rig();
        rig.Player(Vector2.Zero);
        rig.Bandit(new Vector2(100, 0));
        rig.Run(1);
        rig.World.Commands.Enqueue(new AcceptBountyCommand("bandits") { Tick = rig.World.Tick });
        rig.Run(1);

        var builder = new SaveBuilder();
        BountyBoardSave.Write(builder.Game.Camera, rig.World, rig.Data);
        var factions = new List<FactionData> { new() { Id = "player", Name = "Player" } };
        var data = new PopulationData { Db = new GameDatabase(), Sites = [], Factions = factions, PlayerFaction = 0, Relations = FactionRelations.Build(factions) };
        var board = new BountyBoardSystem(data);
        using var world = new SimWorld(new WorldSettings { Seed = 1, Threads = 1, PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), [board]);

        BountyBoardSave.Read(builder.Game.Camera, world, data);

        Assert.Empty(board.Contracts);
    }

    [Fact]
    public void Offers_and_completes_a_named_non_story_bandit()
    {
        using var rig = new Rig(new BountySettings { OfferRefreshTicks = 1, ViewRefreshTicks = 1, OfferAnimals = false });
        var player = rig.Player(Vector2.Zero);
        var target = rig.NamedBandit(new Vector2(100, 0));
        rig.Run(1);

        var hunt = Assert.Single(rig.Board.Published.Offers, o => o.Id.StartsWith("hunt:", StringComparison.Ordinal));
        Assert.Equal("Hunt Razar", hunt.Title);
        Assert.Equal("Kill the target", hunt.Detail);
        rig.World.Commands.Enqueue(new AcceptBountyCommand(hunt.Id) { Tick = rig.World.Tick });
        rig.Run(1);

        var contract = Assert.Single(rig.Board.Contracts);
        Assert.Equal(BountyTargetKind.Character, contract.Kind);
        Assert.Equal(hunt.Id, contract.TargetMarker);
        Assert.Equal(hunt.Id, rig.Cold(target).ContractTarget);

        rig.Board.Died(rig.World, player.Slot, target.Slot);
        Assert.Equal(5000, rig.World.Player.Money);
        Assert.Equal(BountyState.Completed, contract.State);
    }

    [Fact]
    public void A_downed_named_target_offers_capture_and_completes_when_caged()
    {
        using var rig = new Rig(new BountySettings { OfferRefreshTicks = 1, ViewRefreshTicks = 1, OfferAnimals = false });
        _ = rig.Player(Vector2.Zero);
        var target = rig.NamedBandit(new Vector2(100, 0));
        rig.Cold(target).Medical!.KnockOut(100000);
        rig.Run(1);

        var capture = Assert.Single(rig.Board.Published.Offers, o => o.Title.StartsWith("Capture ", StringComparison.Ordinal));
        Assert.Equal("Cage the target", capture.Detail);
        rig.World.Commands.Enqueue(new AcceptBountyCommand(capture.Id) { Tick = rig.World.Tick });
        rig.Run(1);

        var contract = Assert.Single(rig.Board.Contracts);
        Assert.Equal(BountyObjective.Capture, contract.Objective);
        rig.Cold(target).InSomething = Meitou.Simulation.Locks.HeldState.Imprisoned;
        rig.Cold(target).InWhat = "cage";
        rig.Run(1);
        Assert.Equal(6500, rig.World.Player.Money);
        Assert.Equal(BountyState.Completed, contract.State);
    }

    [Fact]
    public void Offers_and_completes_an_animal_species_cull()
    {
        using var rig = new Rig(new BountySettings { OfferRefreshTicks = 1, ViewRefreshTicks = 1, OfferSpecific = false });
        var player = rig.Player(Vector2.Zero);
        var first = rig.Animal(new Vector2(100, 0));
        var second = rig.Animal(new Vector2(100, 10));
        var third = rig.Animal(new Vector2(100, 20));
        rig.Run(1);

        var cull = Assert.Single(rig.Board.Published.Offers, o => o.Id == "cull:beak");
        Assert.Equal("Cull beak", cull.Title);
        Assert.Equal("3 animals", cull.Detail);
        rig.World.Commands.Enqueue(new AcceptBountyCommand(cull.Id) { Tick = rig.World.Tick });
        rig.Run(1);

        var contract = Assert.Single(rig.Board.Contracts);
        Assert.Equal(BountyTargetKind.AnimalSpecies, contract.Kind);
        rig.Board.Died(rig.World, player.Slot, first.Slot);
        rig.Board.Died(rig.World, player.Slot, second.Slot);
        rig.Board.Died(rig.World, player.Slot, third.Slot);
        Assert.Equal(3000, rig.World.Player.Money);
        Assert.Equal(BountyState.Completed, contract.State);
    }

    [Fact]
    public void No_existing_target_offers_a_spawned_squad_and_accept_spawns_it()
    {
        var factions = new List<FactionData>
        {
            new() { Id = "player", Name = "Player", FundamentalType = FundamentalType.Civilian },
            new() { Id = "bandits", Name = "Bandits", FundamentalType = FundamentalType.Bandit, Squads = [new RecordLink("10-t", 1, 0, 0)] },
        };
        var relations = FactionRelations.Build(factions);
        relations.PlayerFaction = PlayerFaction;
        var data = new PopulationData { Db = SyntheticTown.Database(), Sites = [], Factions = factions, PlayerFaction = PlayerFaction, Relations = relations };
        using var population = new PopulationSystem(data, new PopulationSettings { Background = false });
        var board = new BountyBoardSystem(data, new BountySettings { OfferRefreshTicks = 1, ViewRefreshTicks = 1, OfferSpecific = false, OfferAnimals = false })
        {
            Population = population,
        };
        using var world = new SimWorld(new WorldSettings { Seed = 1, Threads = 1, PublishSnapshots = false }, new OpenGroundWalkability((_, _) => 0), [board]);
        world.Characters.Spawn(new CharacterHot { Position = Vector3.Zero, Health = 100 },
            new CharacterCold { Name = "hero", Faction = PlayerFaction, IsPlayer = true, Medical = new MedicalState([]), Fighter = new Fighter(null) }, 0);
        world.RunTicks(1);

        var offer = Assert.Single(board.Published.Offers);
        Assert.Equal("spawn:bandits", offer.Id);
        world.Commands.Enqueue(new AcceptBountyCommand(offer.Id) { Tick = world.Tick });
        world.RunTicks(1);

        var contract = Assert.Single(board.Contracts);
        Assert.Equal(BountyTargetKind.Spawned, contract.Kind);
        Assert.True(contract.TargetMarker is { Length: > 0 });
        Assert.Contains(Enumerable.Range(0, world.Characters.HighWater), slot => world.Characters.Cold(slot)?.ContractTarget == contract.TargetMarker);
    }

    [Fact]
    public void A_named_target_marker_and_contract_round_trip_through_save_state()
    {
        using var rig = new Rig(new BountySettings { OfferRefreshTicks = 1, ViewRefreshTicks = 1, OfferAnimals = false });
        _ = rig.Player(Vector2.Zero);
        var target = rig.NamedBandit(new Vector2(100, 0));
        rig.Run(1);
        var hunt = Assert.Single(rig.Board.Published.Offers, o => o.Id.StartsWith("hunt:", StringComparison.Ordinal));
        rig.World.Commands.Enqueue(new AcceptBountyCommand(hunt.Id) { Tick = rig.World.Tick });
        rig.Run(1);
        _ = target;

        var state = new FcsRecord();
        ContractTargetSave.Write(state, hunt.Id);
        Assert.Equal(hunt.Id, ContractTargetSave.Read(state));
        ContractTargetSave.Write(state, null);
        Assert.Null(ContractTargetSave.Read(state));

        var builder = new SaveBuilder();
        BountyBoardSave.Write(builder.Game.Camera, rig.World, rig.Data);
        using var restored = new Rig();
        BountyBoardSave.Read(builder.Game.Camera, restored.World, restored.Data);
        var contract = Assert.Single(restored.Board.Contracts);
        Assert.Equal(BountyTargetKind.Character, contract.Kind);
        Assert.Equal(hunt.Id, contract.TargetMarker);
        Assert.Equal(BountyState.Accepted, contract.State);
    }

    [Fact]
    public void Standard_systems_register_the_board_and_wire_the_combat_death_hook()
    {
        var data = PopulationData.Create(SyntheticTown.Database(), []);
        var walk = new OpenGroundWalkability((_, _) => 0);
        var systems = StandardSystems.Build(data, walk, new StandardSystemOptions { Parts = StandardParts.Bounties | StandardParts.Combat });

        Assert.NotNull(systems.Bounties);
        Assert.Same(systems.Bounties, systems.Combat!.Deaths);
        Assert.Contains(systems.Bounties, systems.Systems);
    }
}
