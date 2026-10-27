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
    public void A_giver_contract_is_hidden_from_the_faction_tab_and_pays_only_on_turn_in()
    {
        var catalog = new BountyContractCatalog();
        catalog.Register("frontier.test", new BountyContractDefinition
        {
            Id = "frontier.test:bounty/hub",
            Kind = BountyTargetKind.Faction,
            Objective = BountyObjective.Kill,
            Title = "Hub Dust",
            FactionId = "bandits",
            Required = 1,
            Reward = 777,
            Giver = "hub",
        });
        using var rig = new Rig(new BountySettings
        {
            Contracts = catalog,
            OfferRefreshTicks = 1,
            ViewRefreshTicks = 1,
            OfferSpecific = false,
            OfferAnimals = false,
            OfferSpawned = false,
        });
        var player = rig.Player(Vector2.Zero);
        var target = rig.Bandit(new Vector2(100, 0));
        rig.Run(1);

        Assert.DoesNotContain(rig.Board.Published.Offers, offer => offer.Id == "frontier.test:bounty/hub");
        var offer = Assert.Single(rig.Board.PublishedForGiver("hub"));
        Assert.Equal("Hub Dust", offer.Title);
        Assert.True(rig.Board.TryAccept(rig.World, offer.Id));
        var contract = Assert.Single(rig.Board.Contracts);
        Assert.Equal("hub", contract.Giver);
        Assert.Equal(BountyState.Accepted, contract.State);
        Assert.Empty(rig.Board.Published.Active);
        var marker = Assert.Single(rig.Board.Published.Markers);
        Assert.Equal(contract.Id, marker.Id);
        Assert.Equal(100, marker.Position.X, 1);

        rig.Board.Died(rig.World, player.Slot, target.Slot);
        Assert.Equal(BountyState.ReadyToTurnIn, contract.State);
        Assert.Equal(0, rig.World.Player.Money);
        var builder = new SaveBuilder();
        BountyBoardSave.Write(builder.Game.Camera, rig.World, rig.Data);
        using (var restored = new Rig(new BountySettings
        {
            Contracts = catalog,
            OfferRefreshTicks = 1,
            ViewRefreshTicks = 1,
            OfferSpecific = false,
            OfferAnimals = false,
            OfferSpawned = false,
        }))
        {
            BountyBoardSave.Read(builder.Game.Camera, restored.World, restored.Data);
            var saved = Assert.Single(restored.Board.Contracts);
            Assert.Equal(BountyState.ReadyToTurnIn, saved.State);
            Assert.Equal("hub", saved.Giver);
        }
        Assert.True(rig.Board.TurnIn(rig.World, contract.Id));
        Assert.Equal(BountyState.Completed, contract.State);
        Assert.Equal(777, rig.World.Player.Money);
        Assert.Empty(rig.Board.PublishedForGiver("hub"));
        Assert.Empty(rig.Board.Published.Markers);
    }

    [Fact]
    public void A_giver_contract_expires_without_paying()
    {
        var catalog = new BountyContractCatalog();
        catalog.Register("frontier.test", new BountyContractDefinition
        {
            Id = "frontier.test:bounty/expiring",
            Kind = BountyTargetKind.Faction,
            Objective = BountyObjective.Kill,
            Title = "Expiring job",
            FactionId = "bandits",
            Required = 1,
            Reward = 900,
            Giver = "hub",
            DeadlineHours = 1,
        });
        using var rig = new Rig(new BountySettings
        {
            Contracts = catalog, OfferRefreshTicks = 1, ViewRefreshTicks = 1,
            OfferSpecific = false, OfferAnimals = false, OfferSpawned = false,
        });
        _ = rig.Player(Vector2.Zero);
        var giver = rig.NamedBandit(new Vector2(20, 0), "Barman");
        rig.Run(1);

        var offer = Assert.Single(rig.Board.PublishedForGiver("hub"));
        Assert.Equal(1, offer.DeadlineHours);
        Assert.True(rig.Board.TryAccept(rig.World, offer.Id, giver.ToString(), "Barman"));
        var contract = Assert.Single(rig.Board.Contracts);
        Assert.Equal("Barman", contract.GiverName);
        Assert.Equal(giver.ToString(), contract.GiverNpc);
        Assert.Equal(BountyState.Accepted, contract.State);
        Assert.Equal(1, contract.ExpiresAtGameHours - contract.AcceptedAtGameHours, 3);
        Assert.Single(rig.Board.Published.Journal);

        rig.World.GameHours = 39;
        rig.Run(1);

        Assert.Equal(BountyState.Expired, contract.State);
        Assert.Equal("deadline", contract.FailureReason);
        Assert.Equal(0, rig.World.Player.Money);
        Assert.Empty(rig.Board.Published.Journal);
        Assert.Contains(rig.World.TakeMessages(), message => message.Contains("Bounty expired: Expiring job", StringComparison.Ordinal));

        var builder = new SaveBuilder();
        BountyBoardSave.Write(builder.Game.Camera, rig.World, rig.Data);
        using var restored = new Rig(new BountySettings
        {
            Contracts = catalog, OfferRefreshTicks = 1, ViewRefreshTicks = 1,
            OfferSpecific = false, OfferAnimals = false, OfferSpawned = false,
        });
        BountyBoardSave.Read(builder.Game.Camera, restored.World, restored.Data);
        var saved = Assert.Single(restored.Board.Contracts);
        Assert.Equal(BountyState.Expired, saved.State);
        Assert.Equal("deadline", saved.FailureReason);
        Assert.Equal("Barman", saved.GiverName);
        Assert.Equal(giver.ToString(), saved.GiverNpc);
        Assert.Equal(1, saved.ExpiresAtGameHours - saved.AcceptedAtGameHours, 3);
    }

    [Fact]
    public void A_giver_contract_fails_when_its_giver_dies()
    {
        var catalog = new BountyContractCatalog();
        catalog.Register("frontier.test", new BountyContractDefinition
        {
            Id = "frontier.test:bounty/giver-death",
            Kind = BountyTargetKind.Faction,
            Objective = BountyObjective.Kill,
            Title = "Dead giver job",
            FactionId = "bandits",
            Required = 1,
            Reward = 800,
            Giver = "hub",
        });
        using var rig = new Rig(new BountySettings
        {
            Contracts = catalog, OfferRefreshTicks = 1, ViewRefreshTicks = 1,
            OfferSpecific = false, OfferAnimals = false, OfferSpawned = false,
        });
        _ = rig.Player(Vector2.Zero);
        var giver = rig.NamedBandit(new Vector2(20, 0), "Barman");
        rig.Run(1);

        var offer = Assert.Single(rig.Board.PublishedForGiver("hub"));
        Assert.True(rig.Board.TryAccept(rig.World, offer.Id, giver.ToString(), "Barman"));
        var contract = Assert.Single(rig.Board.Contracts);
        rig.Cold(giver).Medical!.Butcher();
        rig.Run(1);

        Assert.Equal(BountyState.Failed, contract.State);
        Assert.Equal("giver_dead", contract.FailureReason);
        Assert.Equal(0, rig.World.Player.Money);
        Assert.Empty(rig.Board.Published.Journal);
    }

    [Fact]
    public void A_content_offer_waits_for_relation_and_rank()
    {
        var catalog = new BountyContractCatalog();
        catalog.Register("frontier.test", new BountyContractDefinition
        {
            Id = "frontier.test:bounty/locked", Kind = BountyTargetKind.Faction, Objective = BountyObjective.Kill,
            Title = "Trusted work", FactionId = "bandits", Required = 1, Reward = 10, Giver = "hub",
            RequiredFactionId = "bandits", MinRelation = 50, MinRank = 1,
        });
        using var rig = new Rig(new BountySettings
        {
            Contracts = catalog, OfferRefreshTicks = 1, ViewRefreshTicks = 1, MaxOffers = 1,
            OfferSpecific = false, OfferAnimals = false, OfferSpawned = false,
        });
        _ = rig.Player(Vector2.Zero);
        _ = rig.Bandit(new Vector2(100, 0));
        rig.World.Player.Faction = 0;
        rig.Run(1);
        Assert.Empty(rig.Board.PublishedForGiver("hub"));

        rig.Data.Relations.Add(0, 1, 60);
        rig.Data.Relations.SetRank(1, 1);
        rig.Run(1);
        Assert.Single(rig.Board.PublishedForGiver("hub"));
    }

    [Fact]
    public void A_content_offer_waits_for_a_completed_prerequisite()
    {
        var catalog = new BountyContractCatalog();
        catalog.Register("frontier.test", new BountyContractDefinition
        {
            Id = "frontier.test:bounty/prereq", Kind = BountyTargetKind.Faction, Objective = BountyObjective.Kill,
            Title = "First job", FactionId = "bandits", Required = 1, Reward = 1,
        });
        catalog.Register("frontier.test", new BountyContractDefinition
        {
            Id = "frontier.test:bounty/locked-prereq", Kind = BountyTargetKind.Faction, Objective = BountyObjective.Kill,
            Title = "Follow-up", FactionId = "bandits", Required = 1, Reward = 2,
            RequiredCompletedContractId = "frontier.test:bounty/prereq",
        });
        using var rig = new Rig(new BountySettings
        {
            Contracts = catalog, OfferRefreshTicks = 1, ViewRefreshTicks = 1, MaxOffers = 1,
            OfferSpecific = false, OfferAnimals = false, OfferSpawned = false,
        });
        var player = rig.Player(Vector2.Zero);
        var target = rig.Bandit(new Vector2(100, 0));
        rig.Run(1);
        var first = Assert.Single(rig.Board.Published.Offers);
        Assert.Equal("frontier.test:bounty/prereq", first.Id);
        rig.World.Commands.Enqueue(new AcceptBountyCommand(first.Id) { Tick = rig.World.Tick });
        rig.Run(1);
        rig.Board.Died(rig.World, player.Slot, target.Slot);
        Assert.Equal(BountyState.Completed, Assert.Single(rig.Board.Contracts).State);

        rig.Run(1);
        Assert.Equal("frontier.test:bounty/locked-prereq", Assert.Single(rig.Board.Published.Offers).Id);
    }

    [Fact]
    public void A_content_contract_rolls_its_reward_range_and_applies_relation()
    {
        var catalog = new BountyContractCatalog();
        catalog.Register("frontier.test", new BountyContractDefinition
        {
            Id = "frontier.test:bounty/reward", Kind = BountyTargetKind.Faction, Objective = BountyObjective.Kill,
            Title = "Paid work", FactionId = "bandits", Required = 1, Reward = 100, RewardMax = 200,
            RelationOnComplete = 5,
        });
        using var rig = new Rig(new BountySettings
        {
            Contracts = catalog, OfferRefreshTicks = 1, ViewRefreshTicks = 1, MaxOffers = 1,
            OfferSpecific = false, OfferAnimals = false, OfferSpawned = false,
        });
        var player = rig.Player(Vector2.Zero);
        var target = rig.Bandit(new Vector2(100, 0));
        rig.World.Player.Faction = 0;
        float before = rig.Data.Relations.Get(0, 1);
        rig.Run(1);

        var offer = Assert.Single(rig.Board.Published.Offers);
        Assert.InRange(offer.Reward, 100, 200);
        rig.World.Commands.Enqueue(new AcceptBountyCommand(offer.Id) { Tick = rig.World.Tick });
        rig.Run(1);
        rig.Board.Died(rig.World, player.Slot, target.Slot);

        Assert.Equal(offer.Reward, rig.World.Player.Money);
        Assert.Equal(5, rig.Data.Relations.Get(0, 1) - before, 3);
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
    public void A_content_character_contract_picks_a_nearby_non_unique_target()
    {
        var catalog = new BountyContractCatalog();
        catalog.Register("frontier.test", new BountyContractDefinition
        {
            Id = "frontier.test:bounty/individual", Kind = BountyTargetKind.Character, Objective = BountyObjective.Kill,
            Title = "Clean out a bandit", FactionId = "bandits", Required = 1, Reward = 700,
        });
        using var rig = new Rig(new BountySettings
        {
            Contracts = catalog, OfferRefreshTicks = 1, ViewRefreshTicks = 1, MaxOffers = 1,
            OfferSpecific = false, OfferAnimals = false, OfferSpawned = false,
        });
        var player = rig.Player(Vector2.Zero);
        var target = rig.Bandit(new Vector2(100, 0), "Dusty");
        rig.Run(1);

        var offer = Assert.Single(rig.Board.Published.Offers);
        Assert.Equal("frontier.test:bounty/individual", offer.Id);
        rig.World.Commands.Enqueue(new AcceptBountyCommand(offer.Id) { Tick = rig.World.Tick });
        rig.Run(1);
        var contract = Assert.Single(rig.Board.Contracts);
        Assert.Equal(BountyTargetKind.Character, contract.Kind);
        Assert.Equal("Dusty", contract.TargetName);
        Assert.Equal(rig.Cold(target).ContractTarget, contract.TargetMarker);
        Assert.Equal(BountyState.Accepted, contract.State);

        rig.Board.Died(rig.World, player.Slot, target.Slot);
        Assert.Equal(BountyState.Completed, contract.State);
        Assert.Equal(700, rig.World.Player.Money);
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
