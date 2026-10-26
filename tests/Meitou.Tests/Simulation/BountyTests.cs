using System.Numerics;
using Meitou.Data;
using Meitou.Data.Gameplay;
using Meitou.Data.Save;
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Bounties;
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

        public CharacterId Spawn(int faction, bool player, Vector2 at, string name)
        {
            var cold = new CharacterCold { Name = name, Faction = faction, IsPlayer = player, Medical = new MedicalState([]) };
            var id = World.Characters.Spawn(new CharacterHot { Position = new Vector3(at.X, 0, at.Y), Health = 100 }, cold, 0);
            if (faction == Bandits) bandits.Add(id);
            return id;
        }

        public CharacterId Player(Vector2 at) => Spawn(PlayerFaction, true, at, "hero");
        public CharacterId Bandit(Vector2 at, string name = "bandit") => Spawn(Bandits, false, at, name);
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
