using Meitou.Data;
using Meitou.Data.Gameplay;

namespace Meitou.Simulation.Bounties;

/// <summary>How a bounty contract stands.</summary>
public enum BountyState : byte { Accepted = 0, Completed = 1 }

/// <summary>
/// A player bounty contract: kill <see cref="Required"/> members of <see cref="Faction"/> for the reward. The faction is the target's faction; its ID is what a save
/// stores, so a changed load order resolves to the new faction position or drops the contract.
/// </summary>
public sealed class BountyContract
{
    public required int Faction { get; init; }
    public required string FactionId { get; init; }
    public required string FactionName { get; init; }
    public required int Required { get; init; }
    public required int Reward { get; init; }
    public int Progress { get; set; }
    public BountyState State { get; set; }
    public long CompletedTick { get; set; } = -1;

    internal void Hash(ref StateHasher h)
    {
        h.Add(Faction);
        h.Add(Required);
        h.Add(Reward);
        h.Add(Progress);
        h.Add((byte)State);
    }
}

/// <summary>One offer in the faction tab: kill <see cref="Required"/> of the faction for <see cref="Reward"/>; <see cref="Distance"/> is to the nearest living member.</summary>
public readonly record struct BountyOfferView(string Id, string FactionName, int Required, int Reward, float Distance);

/// <summary>One accepted contract in the faction tab.</summary>
public readonly record struct BountyContractView(string Id, string FactionName, int Progress, int Required, int Reward, float Distance);

/// <summary>The published bounty board (built in the simulation's serial phase, read by the host).</summary>
public sealed record BountyBoardView(IReadOnlyList<BountyOfferView> Offers, IReadOnlyList<BountyContractView> Active)
{
    public static BountyBoardView Empty { get; } = new([], []);
}

/// <summary>Accepting a board offer; the UI sends it through the normal command queue.</summary>
public sealed record AcceptBountyCommand(string ContractId) : SimCommand;

/// <summary>Giving up an accepted contract; the faction can be offered again.</summary>
public sealed record AbandonBountyCommand(string ContractId) : SimCommand;

public sealed record BountySettings
{
    /// <summary>Members of the target faction a contract asks for.</summary>
    public int Kills { get; init; } = 3;
    /// <summary>The reward paid when the last kill is counted.</summary>
    public int Reward { get; init; } = 4500;
    /// <summary>Only factions with a living member within this range of a player character are offered.</summary>
    public float OfferRange { get; init; } = 20000;
    public int MaxOffers { get; init; } = 3;
    /// <summary>Ticks between looks for new offers.</summary>
    public int OfferRefreshTicks { get; init; } = 150;
    /// <summary>Ticks between rebuilt views (distance changes as the player moves).</summary>
    public int ViewRefreshTicks { get; init; } = 15;
}

/// <summary>
/// Faction bounties (the first real bounty contract loop, docs/simulation.md "Crimes and bounties"): offers nearby bandit factions, counts the player's killing blows
/// through <see cref="Combat.ICombatDeathSink"/>, pays on the last one, and publishes offers/progress for the faction tab. Accepted and completed contracts are saved
/// in the CAMERA record by <see cref="Saving.BountyBoardSave"/>; unfinished offers are derived again from the world.
/// </summary>
public sealed class BountyBoardSystem(PopulationData data, BountySettings? settings = null) : ITickSystem, IStateHashed, Combat.ICombatDeathSink
{
    readonly List<BountyOffer> offers = [];
    readonly List<BountyContract> contracts = [];
    readonly BountySettings settings = settings ?? new BountySettings();
    BountyBoardView published = BountyBoardView.Empty;

    public BountySettings Settings => settings;
    public IReadOnlyList<BountyContract> Contracts => contracts;
    public BountyBoardView Published => published;

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is AcceptBountyCommand accept) Accept(world, accept.ContractId);
            else if (command is AbandonBountyCommand abandon) Abandon(world, abandon.ContractId);
        }
    }

    public void SlowWorld(World world)
    {
        if (world.Tick % Math.Max(1, settings.OfferRefreshTicks) == 0) RefreshOffers(world);
        if (world.Tick % Math.Max(1, settings.ViewRefreshTicks) == 0) Publish(world);
    }

    /// <summary>A killing blow from a player's character (Combat's serial death hook).</summary>
    public void Died(World world, int attacker, int defender)
    {
        if ((uint)attacker >= (uint)world.Characters.HighWater || (uint)defender >= (uint)world.Characters.HighWater) return;
        if (world.Characters.Cold(attacker) is not { IsPlayer: true } || world.Characters.Cold(defender) is not { } victim) return;
        for (int i = 0; i < contracts.Count; i++)
        {
            var contract = contracts[i];
            if (contract.State != BountyState.Accepted || contract.Faction != victim.Faction) continue;
            contract.Progress++;
            if (contract.Progress < contract.Required) continue;
            Complete(world, contract, attacker);
        }
    }

    void Complete(World world, BountyContract contract, int attacker)
    {
        contract.Progress = contract.Required;
        contract.State = BountyState.Completed;
        contract.CompletedTick = world.Tick;
        world.Player.Money += contract.Reward;
        world.Post($"Bounty complete: {contract.FactionName}. Received c.{contract.Reward:N0}");
        if (attacker >= 0 && attacker < world.Characters.Next.Length)
            world.EmitSound(SoundKind.BountyPaid, attacker, world.Characters.Next[attacker].Position);
        Publish(world);
    }

    void Accept(World world, string id)
    {
        int at = offers.FindIndex(o => o.Id == id);
        if (at < 0 || contracts.Exists(c => c.Faction == offers[at].Faction)) return;
        var offer = offers[at];
        offers.RemoveAt(at);
        contracts.Add(new BountyContract
        {
            Faction = offer.Faction,
            FactionId = offer.Id,
            FactionName = offer.FactionName,
            Required = offer.Required,
            Reward = offer.Reward,
        });
        world.Post($"Bounty accepted: {offer.FactionName}");
        Publish(world);
    }

    void Abandon(World world, string id)
    {
        int at = contracts.FindIndex(c => c.FactionId == id && c.State == BountyState.Accepted);
        if (at < 0) return;
        string name = contracts[at].FactionName;
        contracts.RemoveAt(at);
        world.Post($"Bounty abandoned: {name}");
        Publish(world);
    }

    void RefreshOffers(World world)
    {
        var players = PlayerPositions(world);
        offers.RemoveAll(o => float.IsPositiveInfinity(Nearest(world, o.Faction, players)));
        if (offers.Count < settings.MaxOffers)
        {
            var candidates = new List<(int Faction, float Distance)>();
            for (int f = 0; f < data.Factions.Count; f++)
            {
                var faction = data.Factions[f];
                if (faction.FundamentalType != FundamentalType.Bandit || faction.NotReal) continue;
                if (offers.Exists(o => o.Faction == f) || contracts.Exists(c => c.Faction == f)) continue;
                float distance = Nearest(world, f, players);
                if (!float.IsPositiveInfinity(distance) && distance <= settings.OfferRange) candidates.Add((f, distance));
            }
            candidates.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : string.CompareOrdinal(data.Factions[a.Faction].Name, data.Factions[b.Faction].Name));
            foreach (var (faction, _) in candidates)
            {
                if (offers.Count >= settings.MaxOffers) break;
                offers.Add(new BountyOffer(faction, data.Factions[faction].Id, data.Factions[faction].Name, settings.Kills, settings.Reward));
            }
        }
        Publish(world);
    }

    void Publish(World world)
    {
        var players = PlayerPositions(world);
        var viewOffers = new List<BountyOfferView>(offers.Count);
        foreach (var offer in offers)
            viewOffers.Add(new BountyOfferView(offer.Id, offer.FactionName, offer.Required, offer.Reward, Nearest(world, offer.Faction, players)));
        var active = new List<BountyContractView>();
        foreach (var contract in contracts)
            if (contract.State == BountyState.Accepted)
                active.Add(new BountyContractView(contract.FactionId, contract.FactionName, contract.Progress, contract.Required, contract.Reward, Nearest(world, contract.Faction, players)));
        published = new BountyBoardView(viewOffers, active);
    }

    static List<System.Numerics.Vector3> PlayerPositions(World world)
    {
        var players = new List<System.Numerics.Vector3>();
        for (int s = 0; s < world.Characters.HighWater; s++)
            if (world.Characters.Next[s].Alive && world.Characters.Cold(s) is { IsPlayer: true, Medical.Dead: false })
                players.Add(world.Characters.Next[s].Position);
        return players;
    }

    static float Nearest(World world, int faction, List<System.Numerics.Vector3> players)
    {
        if (players.Count == 0) return float.PositiveInfinity;
        float best = float.PositiveInfinity;
        for (int s = 0; s < world.Characters.HighWater; s++)
        {
            if (!world.Characters.Next[s].Alive || world.Characters.Cold(s) is not { Medical.Dead: false } c || c.Faction != faction || c.IsPlayer) continue;
            var at = world.Characters.Next[s].Position;
            foreach (var player in players)
            {
                float dx = at.X - player.X, dz = at.Z - player.Z;
                float d = dx * dx + dz * dz;
                if (d < best) best = d;
            }
        }
        return best == float.PositiveInfinity ? best : MathF.Sqrt(best);
    }

    void IStateHashed.Hash(ref StateHasher h)
    {
        if (contracts.Count == 0) return;
        h.Add(0x426F756E);   // "Boun"
        h.Add(contracts.Count);
        foreach (var contract in contracts) contract.Hash(ref h);
    }

    sealed class BountyOffer(int faction, string id, string factionName, int required, int reward)
    {
        public int Faction { get; } = faction;
        public string Id { get; } = id;
        public string FactionName { get; } = factionName;
        public int Required { get; } = required;
        public int Reward { get; } = reward;
    }

    /// <summary>Restores one saved contract; called by <see cref="Saving.BountyBoardSave"/> on load.</summary>
    public void Restore(int faction, int required, int progress, int reward, bool completed)
    {
        if ((uint)faction >= (uint)data.Factions.Count || contracts.Exists(c => c.Faction == faction)) return;
        var dataFaction = data.Factions[faction];
        contracts.Add(new BountyContract
        {
            Faction = faction,
            FactionId = dataFaction.Id,
            FactionName = dataFaction.Name,
            Required = Math.Max(1, required),
            Reward = Math.Max(0, reward),
            Progress = Math.Clamp(progress, 0, Math.Max(1, required)),
            State = completed ? BountyState.Completed : BountyState.Accepted,
            CompletedTick = completed ? 0 : -1,
        });
    }

    /// <summary>Rebuilds the display after a load, before the first tick.</summary>
    public void PublishForLoad(World world) => Publish(world);
}
