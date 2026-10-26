using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;

namespace Meitou.Simulation.Bounties;

/// <summary>How a contract stands.</summary>
public enum BountyState : byte { Accepted = 0, Completed = 1 }

/// <summary>What a contract targets: a whole faction, one existing character, or the animals of one species.</summary>
public enum BountyTargetKind : byte { Faction = 0, Character = 1, AnimalSpecies = 2, Spawned = 3 }

/// <summary>What the player must do to the target. Kill is implemented now; Capture is part of the same state model.</summary>
public enum BountyObjective : byte { Kill = 0, Capture = 1 }

/// <summary>
/// One player contract. A faction contract counts kills of any member; an animal contract counts kills of one species; a character contract follows one marked,
/// non-story NPC (or a later spawned target) through its stable <see cref="TargetMarker"/>.
/// </summary>
public sealed class BountyContract
{
    public required string Id { get; init; }
    public required BountyTargetKind Kind { get; init; }
    public required BountyObjective Objective { get; init; }
    public required string Title { get; init; }
    public required string TargetName { get; init; }
    /// <summary>The animal or character record the target belongs to (null for a faction contract).</summary>
    public string? TargetRecord { get; init; }
    /// <summary>The stable marker on a specific character (null for group/species contracts).</summary>
    public string? TargetMarker { get; init; }
    /// <summary>The target faction's position, when known; also the employer/display faction for a character contract.</summary>
    public int Faction { get; init; } = -1;
    public string FactionId { get; init; } = "";
    public required int Required { get; init; }
    public required int Reward { get; init; }
    public int Progress { get; set; }
    public BountyState State { get; set; }
    public long CompletedTick { get; set; } = -1;

    internal void Hash(ref StateHasher h)
    {
        h.Add((byte)Kind);
        h.Add((byte)Objective);
        h.Add(Faction);
        h.Add(Required);
        h.Add(Reward);
        h.Add(Progress);
        h.Add((byte)State);
        h.Add(TargetRecord is { } record ? Rng.StableHash(record) : 0UL);
        h.Add(TargetMarker is { } marker ? Rng.StableHash(marker) : 0UL);
    }
}

/// <summary>One offer in the faction tab.</summary>
public readonly record struct BountyOfferView(string Id, string Title, string Detail, int Required, int Reward, float Distance);

/// <summary>One accepted contract in the faction tab.</summary>
public readonly record struct BountyContractView(string Id, string Title, int Progress, int Required, int Reward, float Distance);

/// <summary>The published bounty board (built in the simulation's serial phase, read by the host).</summary>
public sealed record BountyBoardView(IReadOnlyList<BountyOfferView> Offers, IReadOnlyList<BountyContractView> Active)
{
    public static BountyBoardView Empty { get; } = new([], []);
}

/// <summary>Accepting a board offer; the UI sends it through the normal command queue.</summary>
public sealed record AcceptBountyCommand(string ContractId) : SimCommand;

/// <summary>Giving up an accepted contract; the faction or target can be offered again.</summary>
public sealed record AbandonBountyCommand(string ContractId) : SimCommand;

public sealed record BountySettings
{
    public int FactionKills { get; init; } = 3;
    public int FactionReward { get; init; } = 4500;
    public int AnimalKills { get; init; } = 3;
    public int AnimalReward { get; init; } = 3000;
    public int SpecificReward { get; init; } = 5000;
    /// <summary>Offer a hunt for one existing non-story bandit when one is near.</summary>
    public bool OfferSpecific { get; init; } = true;
    /// <summary>Offer a cull of one animal species when its members are near.</summary>
    public bool OfferAnimals { get; init; } = true;
    public float OfferRange { get; init; } = 20000;
    public int MaxOffers { get; init; } = 3;
    /// <summary>Ticks between looks for new offers.</summary>
    public int OfferRefreshTicks { get; init; } = 150;
    /// <summary>Ticks between rebuilt views (distance changes as the player moves).</summary>
    public int ViewRefreshTicks { get; init; } = 15;
}

/// <summary>
/// The contract board (the first player-facing task loop, docs/simulation.md "Crimes and bounties"). It offers faction-group kills, animal-species culls and a
/// hunt for one existing non-story bandit; a player killing blow through <see cref="Combat.ICombatDeathSink"/> advances the matching contract, and a specific
/// character target can also be completed by caging it (the Capture objective). Accepted and completed contracts are saved in the CAMERA record by
/// <see cref="Saving.BountyBoardSave"/>; offers are derived again from the world. Named targets carry a stable marker saved on the character
/// (<see cref="Saving.ContractTargetSave"/>), so an NPC that unloads with its platoon comes back as the same contract target.
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
        CheckCharacterTargets(world);
        if (world.Tick % Math.Max(1, settings.ViewRefreshTicks) == 0)
        {
            ClearOrphanMarkers(world);
            Publish(world);
        }
    }

    /// <summary>A killing blow from a player's character (Combat's serial death hook).</summary>
    public void Died(World world, int attacker, int defender)
    {
        if ((uint)attacker >= (uint)world.Characters.HighWater || (uint)defender >= (uint)world.Characters.HighWater) return;
        if (world.Characters.Cold(attacker) is not { IsPlayer: true } || world.Characters.Cold(defender) is not { } victim) return;
        for (int i = 0; i < contracts.Count; i++)
        {
            var contract = contracts[i];
            if (contract.State != BountyState.Accepted || contract.Objective != BountyObjective.Kill) continue;
            bool matches = contract.Kind switch
            {
                BountyTargetKind.Faction => victim.Faction == contract.Faction,
                BountyTargetKind.AnimalSpecies => victim.Fighter is { IsAnimal: true } && victim.RecordId == contract.TargetRecord,
                BountyTargetKind.Character or BountyTargetKind.Spawned => victim.ContractTarget is { } marker && marker == contract.TargetMarker,
                _ => false,
            };
            if (!matches) continue;
            contract.Progress++;
            if (contract.Progress >= contract.Required) Complete(world, contract, attacker);
        }
    }

    /// <summary>Specific targets can be caged (Capture) or killed by anyone; contracts over groups count only the player's killing blows.</summary>
    void CheckCharacterTargets(World world)
    {
        for (int i = 0; i < contracts.Count; i++)
        {
            var contract = contracts[i];
            if (contract.State != BountyState.Accepted || contract.Kind is not (BountyTargetKind.Character or BountyTargetKind.Spawned) || contract.TargetMarker is not { } marker) continue;
            if (!TryFindMarker(world, marker, out int slot, out var target)) continue;
            if (contract.Objective == BountyObjective.Capture && target.IsImprisoned) Complete(world, contract, -1);
            else if (contract.Objective == BountyObjective.Kill && target.Medical is { Dead: true }) Complete(world, contract, -1);
        }
    }

    void Complete(World world, BountyContract contract, int attacker)
    {
        if (contract.State == BountyState.Completed) return;
        contract.Progress = contract.Required;
        contract.State = BountyState.Completed;
        contract.CompletedTick = world.Tick;
        world.Player.Money += contract.Reward;
        world.Post($"Bounty complete: {contract.Title}. Received c.{contract.Reward:N0}");
        if (attacker >= 0 && attacker < world.Characters.Next.Length)
            world.EmitSound(SoundKind.BountyPaid, attacker, world.Characters.Next[attacker].Position);
        Publish(world);
    }

    void Accept(World world, string id)
    {
        int at = offers.FindIndex(o => o.Id == id);
        if (at < 0 || contracts.Exists(c => c.Id == id)) return;
        var offer = offers[at];
        if (offer.Kind is BountyTargetKind.Character or BountyTargetKind.Spawned)
        {
            if (!TryFindTarget(world, offer.Target, out _, out var target) || target.ContractTarget is not null || target.IsImprisoned || target.Medical is not { Dead: false }) return;
            SetMarker(target, offer.TargetMarker!);
        }
        offers.RemoveAt(at);
        contracts.Add(new BountyContract
        {
            Id = offer.Id,
            Kind = offer.Kind,
            Objective = offer.Objective,
            Title = offer.Title,
            TargetName = offer.TargetName,
            TargetRecord = offer.TargetRecord,
            TargetMarker = offer.TargetMarker,
            Faction = offer.Faction,
            FactionId = offer.FactionId,
            Required = offer.Required,
            Reward = offer.Reward,
        });
        world.Post($"Bounty accepted: {offer.Title}");
        Publish(world);
    }

    void Abandon(World world, string id)
    {
        int at = contracts.FindIndex(c => c.Id == id && c.State == BountyState.Accepted);
        if (at < 0) return;
        var contract = contracts[at];
        if (contract.TargetMarker is { } marker) ClearMarker(world, marker);
        contracts.RemoveAt(at);
        world.Post($"Bounty abandoned: {contract.Title}");
        Publish(world);
    }

    void RefreshOffers(World world)
    {
        var players = PlayerPositions(world);
        offers.RemoveAll(o => float.IsPositiveInfinity(OfferDistance(world, o, players)));
        if (offers.Count < settings.MaxOffers && settings.OfferSpecific && !HasKind(BountyTargetKind.Character))
            if (FindSpecificTarget(world, players) is { } target) offers.Add(CharacterOffer(target, world.Seed));
        if (offers.Count < settings.MaxOffers && settings.OfferAnimals && !HasKind(BountyTargetKind.AnimalSpecies))
            if (FindAnimalSpecies(world, players) is { } species) offers.Add(AnimalOffer(species, data));
        if (offers.Count < settings.MaxOffers)
        {
            var candidates = new List<(int Faction, float Distance)>();
            for (int f = 0; f < data.Factions.Count; f++)
            {
                var faction = data.Factions[f];
                if (faction.FundamentalType != FundamentalType.Bandit || faction.NotReal) continue;
                if (offers.Exists(o => o.Faction == f) || contracts.Exists(c => c.Faction == f)) continue;
                float distance = NearestFaction(world, f, players);
                if (!float.IsPositiveInfinity(distance) && distance <= settings.OfferRange) candidates.Add((f, distance));
            }
            candidates.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : string.CompareOrdinal(data.Factions[a.Faction].Name, data.Factions[b.Faction].Name));
            foreach (var (faction, _) in candidates)
            {
                if (offers.Count >= settings.MaxOffers) break;
                offers.Add(FactionOffer(faction));
            }
        }
        Publish(world);
    }

    bool HasKind(BountyTargetKind kind) =>
        offers.Exists(o => o.Kind == kind) || contracts.Exists(c => c.Kind == kind && c.State == BountyState.Accepted);

    BountyOffer FactionOffer(int faction) => new(
        data.Factions[faction].Id, BountyTargetKind.Faction, BountyObjective.Kill, data.Factions[faction].Name, data.Factions[faction].Name,
        $"{settings.FactionKills} kills", null, null, CharacterId.None, faction, data.Factions[faction].Id, settings.FactionKills, settings.FactionReward);

    BountyOffer AnimalOffer((string Record, int Count, float Distance) species, PopulationData db)
    {
        string name = db.Db.Find(species.Record)?.Name ?? species.Record;
        return new("cull:" + species.Record, BountyTargetKind.AnimalSpecies, BountyObjective.Kill, "Cull " + name, name,
            $"{settings.AnimalKills} animals", species.Record, null, CharacterId.None, -1, "", settings.AnimalKills, settings.AnimalReward);
    }

    BountyOffer CharacterOffer((CharacterId Id, CharacterCold Cold, float Distance) target, ulong seed)
    {
        string marker = $"hunt:{seed}:{target.Id.Slot}:{target.Id.Generation}";
        int faction = target.Cold.Faction;
        string factionId = (uint)faction < (uint)data.Factions.Count ? data.Factions[faction].Id : "";
        return new(marker, BountyTargetKind.Character, BountyObjective.Kill, "Hunt " + target.Cold.Name, target.Cold.Name, "Kill the target",
            target.Cold.RecordId, marker, target.Id, faction, factionId, 1, settings.SpecificReward);
    }

    void Publish(World world)
    {
        var players = PlayerPositions(world);
        var viewOffers = new List<BountyOfferView>(offers.Count);
        foreach (var offer in offers)
            viewOffers.Add(new BountyOfferView(offer.Id, offer.Title, offer.Detail, offer.Required, offer.Reward, OfferDistance(world, offer, players)));
        var active = new List<BountyContractView>();
        foreach (var contract in contracts)
            if (contract.State == BountyState.Accepted)
                active.Add(new BountyContractView(contract.Id, contract.Title, contract.Progress, contract.Required, contract.Reward, ContractDistance(world, contract, players)));
        published = new BountyBoardView(viewOffers, active);
    }

    float OfferDistance(World world, BountyOffer offer, List<Vector3> players) => offer.Kind switch
    {
        BountyTargetKind.Faction => NearestFaction(world, offer.Faction, players),
        BountyTargetKind.AnimalSpecies => NearestAnimal(world, offer.TargetRecord, players),
        BountyTargetKind.Character => NearestCharacter(world, offer.Target, players),
        BountyTargetKind.Spawned => NearestMarker(world, offer.TargetMarker, players),
        _ => float.PositiveInfinity,
    };

    float ContractDistance(World world, BountyContract contract, List<Vector3> players) => contract.Kind switch
    {
        BountyTargetKind.Faction => NearestFaction(world, contract.Faction, players),
        BountyTargetKind.AnimalSpecies => NearestAnimal(world, contract.TargetRecord, players),
        BountyTargetKind.Character or BountyTargetKind.Spawned => NearestMarker(world, contract.TargetMarker, players),
        _ => float.PositiveInfinity,
    };

    static List<Vector3> PlayerPositions(World world)
    {
        var players = new List<Vector3>();
        for (int s = 0; s < world.Characters.HighWater; s++)
            if (world.Characters.Next[s].Alive && world.Characters.Cold(s) is { IsPlayer: true, Medical.Dead: false })
                players.Add(world.Characters.Next[s].Position);
        return players;
    }

    static float NearestFaction(World world, int faction, List<Vector3> players)
    {
        if (players.Count == 0) return float.PositiveInfinity;
        float best = float.PositiveInfinity;
        for (int s = 0; s < world.Characters.HighWater; s++)
        {
            if (!world.Characters.Next[s].Alive || world.Characters.Cold(s) is not { Medical.Dead: false } c || c.Faction != faction || c.IsPlayer) continue;
            best = MathF.Min(best, Distance(players, world.Characters.Next[s].Position));
        }
        return best;
    }

    static float NearestAnimal(World world, string? record, List<Vector3> players)
    {
        if (players.Count == 0 || record is null) return float.PositiveInfinity;
        float best = float.PositiveInfinity;
        for (int s = 0; s < world.Characters.HighWater; s++)
        {
            if (!world.Characters.Next[s].Alive || world.Characters.Cold(s) is not { Medical.Dead: false, Fighter.IsAnimal: true } c || c.RecordId != record) continue;
            best = MathF.Min(best, Distance(players, world.Characters.Next[s].Position));
        }
        return best;
    }

    static float NearestMarker(World world, string? marker, List<Vector3> players)
    {
        if (players.Count == 0 || marker is null) return float.PositiveInfinity;
        float best = float.PositiveInfinity;
        for (int s = 0; s < world.Characters.HighWater; s++)
        {
            if (!world.Characters.Next[s].Alive || world.Characters.Cold(s) is not { Medical.Dead: false } c || c.ContractTarget != marker) continue;
            best = MathF.Min(best, Distance(players, world.Characters.Next[s].Position));
        }
        return best;
    }

    static float NearestCharacter(World world, CharacterId id, List<Vector3> players)
    {
        if (players.Count == 0 || id.IsNone || !TryFindTarget(world, id, out int slot, out var target) || target.Medical is not { Dead: false } || target.IsImprisoned)
            return float.PositiveInfinity;
        return Distance(players, world.Characters.Next[slot].Position);
    }

    static float Distance(List<Vector3> players, Vector3 at)
    {
        float best = float.PositiveInfinity;
        foreach (var player in players)
        {
            float dx = at.X - player.X, dz = at.Z - player.Z;
            best = MathF.Min(best, dx * dx + dz * dz);
        }
        return MathF.Sqrt(best);
    }

    (CharacterId Id, CharacterCold Cold, float Distance)? FindSpecificTarget(World world, List<Vector3> players)
    {
        (CharacterId Id, CharacterCold Cold, float Distance)? best = null;
        for (int s = 0; s < world.Characters.HighWater; s++)
        {
            if (!world.Characters.Next[s].Alive) continue;
            var id = world.Characters.IdOf(s);
            if (world.Characters.Cold(s) is not { IsPlayer: false, Medical.Dead: false, IsImprisoned: false, CarriedBy.IsNone: true, ContractTarget: null, Fighter.IsAnimal: false } c) continue;
            if (!BanditLike(c) || world.Uniques.RecordOf(id) is not null) continue;
            if (data.Db.Find(c.RecordId) is { } record && Meitou.Simulation.UniqueCharacters.IsUnique(record)) continue;   // leave story NPCs alone
            float distance = Distance(players, world.Characters.Next[s].Position);
            if (distance > settings.OfferRange || best is { } b && b.Distance <= distance) continue;
            best = (id, c, distance);
        }
        return best;
    }

    (string Record, int Count, float Distance)? FindAnimalSpecies(World world, List<Vector3> players)
    {
        var found = new Dictionary<string, (int Count, float Distance)>(StringComparer.Ordinal);
        for (int s = 0; s < world.Characters.HighWater; s++)
        {
            if (!world.Characters.Next[s].Alive) continue;
            if (world.Characters.Cold(s) is not { IsPlayer: false, Medical.Dead: false, IsImprisoned: false, CarriedBy.IsNone: true, Fighter.IsAnimal: true } c) continue;
            float distance = Distance(players, world.Characters.Next[s].Position);
            if (distance > settings.OfferRange) continue;
            found[c.RecordId] = found.TryGetValue(c.RecordId, out var old) ? (old.Count + 1, MathF.Min(old.Distance, distance)) : (1, distance);
        }
        (string Record, int Count, float Distance)? best = null;
        foreach (var (record, value) in found)
        {
            if (value.Count < settings.AnimalKills) continue;
            if (best is { } b && b.Distance <= value.Distance) continue;
            best = (record, value.Count, value.Distance);
        }
        return best;
    }

    bool BanditLike(CharacterCold c) =>
        c.NpcClass == FundamentalType.Bandit ||
        (uint)c.Faction < (uint)data.Factions.Count && data.Factions[c.Faction].FundamentalType == FundamentalType.Bandit;

    static bool TryFindTarget(World world, CharacterId id, out int slot, out CharacterCold target)
    {
        target = null!;
        if (!world.Characters.TryResolveNext(id, out slot) || world.Characters.Cold(slot) is not { } found) return false;
        target = found;
        return true;
    }

    static bool TryFindMarker(World world, string marker, out int slot, out CharacterCold target)
    {
        slot = -1;
        target = null!;
        for (int s = 0; s < world.Characters.HighWater; s++)
            if (world.Characters.Cold(s) is { } c && c.ContractTarget == marker)
            {
                (slot, target) = (s, c);
                return true;
            }
        return false;
    }

    static void SetMarker(CharacterCold target, string marker)
    {
        target.ContractTarget = marker;
        if (target.Save?.Source.GameState is { } state) Saving.ContractTargetSave.Write(state, marker);
    }

    static void ClearMarker(World world, string marker)
    {
        for (int s = 0; s < world.Characters.HighWater; s++)
            if (world.Characters.Cold(s) is { ContractTarget: { } current } c && current == marker) ClearMarker(c);
    }

    static void ClearMarker(CharacterCold target)
    {
        target.ContractTarget = null;
        if (target.Save?.Source.GameState is { } state) Saving.ContractTargetSave.Write(state, null);
    }

    void ClearOrphanMarkers(World world)
    {
        for (int s = 0; s < world.Characters.HighWater; s++)
        {
            if (world.Characters.Cold(s) is not { ContractTarget: { } marker } c) continue;
            if (!contracts.Exists(contract => contract.TargetMarker == marker)) ClearMarker(c);
        }
    }

    void IStateHashed.Hash(ref StateHasher h)
    {
        if (contracts.Count == 0) return;
        h.Add(0x426F756E);   // "Boun"
        h.Add(contracts.Count);
        foreach (var contract in contracts) contract.Hash(ref h);
    }

    sealed class BountyOffer(string id, BountyTargetKind kind, BountyObjective objective, string title, string targetName, string detail, string? targetRecord, string? targetMarker,
        CharacterId target, int faction, string factionId, int required, int reward)
    {
        public string Id { get; } = id;
        public BountyTargetKind Kind { get; } = kind;
        public BountyObjective Objective { get; } = objective;
        public string Title { get; } = title;
        public string TargetName { get; } = targetName;
        public string Detail { get; } = detail;
        public string? TargetRecord { get; } = targetRecord;
        public string? TargetMarker { get; } = targetMarker;
        public CharacterId Target { get; } = target;
        public int Faction { get; } = faction;
        public string FactionId { get; } = factionId;
        public int Required { get; } = required;
        public int Reward { get; } = reward;
    }

    /// <summary>Restores one saved contract; called by <see cref="Saving.BountyBoardSave"/> on load.</summary>
    public void Restore(string id, BountyTargetKind kind, BountyObjective objective, string title, string targetName, string? targetRecord, string? targetMarker,
        int faction, int required, int progress, int reward, bool completed)
    {
        if (contracts.Exists(c => c.Id == id)) return;
        required = Math.Max(1, required);
        contracts.Add(new BountyContract
        {
            Id = id,
            Kind = kind,
            Objective = objective,
            Title = string.IsNullOrWhiteSpace(title) ? targetName : title,
            TargetName = targetName,
            TargetRecord = targetRecord,
            TargetMarker = targetMarker,
            Faction = (uint)faction < (uint)data.Factions.Count ? faction : -1,
            FactionId = (uint)faction < (uint)data.Factions.Count ? data.Factions[faction].Id : "",
            Required = required,
            Reward = Math.Max(0, reward),
            Progress = Math.Clamp(progress, 0, required),
            State = completed ? BountyState.Completed : BountyState.Accepted,
            CompletedTick = completed ? 0 : -1,
        });
    }

    /// <summary>Rebuilds the display after a load, before the first tick.</summary>
    public void PublishForLoad(World world) => Publish(world);
}
