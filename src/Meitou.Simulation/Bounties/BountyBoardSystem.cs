using System.Numerics;
using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Gameplay;

namespace Meitou.Simulation.Bounties;

/// <summary>How a contract stands. A giver contract becomes Ready when its target is done and pays only when turned in.</summary>
public enum BountyState : byte { Accepted = 0, Completed = 1, ReadyToTurnIn = 2, Failed = 3, Expired = 4 }

public static class BountyStates
{
    /// <summary>Accepted or ready: the player still owns the job. Terminal states are completed, failed and expired.</summary>
    public static bool IsOpen(this BountyState state) => state is BountyState.Accepted or BountyState.ReadyToTurnIn;
}

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
    public required string Title { get; set; }
    public required string TargetName { get; set; }
    /// <summary>The animal or character record the target belongs to (null for a faction contract).</summary>
    public string? TargetRecord { get; init; }
    /// <summary>The stable marker on a specific character (null for group/species contracts).</summary>
    public string? TargetMarker { get; init; }
    /// <summary>The target faction's position, when known; also the employer/display faction for a character contract.</summary>
    public int Faction { get; init; } = -1;
    public string FactionId { get; init; } = "";
    /// <summary>The bartender/giver key; empty: the global Faction tab and immediate payment.</summary>
    public string Giver { get; init; } = "";
    /// <summary>The giver character's stable id string when the framework supplied it; empty for older saves and global offers.</summary>
    public string GiverNpc { get; set; } = "";
    /// <summary>The giver's display name for the journal; the key is the fallback.</summary>
    public string GiverName { get; set; } = "";
    /// <summary>The offer detail shown in the journal.</summary>
    public string Detail { get; init; } = "";
    public required int Required { get; init; }
    public required int Reward { get; init; }
    public int Progress { get; set; }
    public BountyState State { get; set; }
    /// <summary>Game hours the contract was accepted at; -1 for an old save.</summary>
    public double AcceptedAtGameHours { get; set; } = -1;
    /// <summary>Game hours the contract expires at; -1: no deadline.</summary>
    public double ExpiresAtGameHours { get; set; } = -1;
    /// <summary>A stable failure reason for <see cref="BountyState.Failed"/>; empty otherwise.</summary>
    public string FailureReason { get; set; } = "";
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
        if (Giver.Length > 0) h.Add(Rng.StableHash(Giver));
    }
}

/// <summary>One offer in the faction tab.</summary>
public readonly record struct BountyOfferView(string Id, string Title, string Detail, int Required, int Reward, float Distance, int DeadlineHours = 0);

/// <summary>One accepted contract in the faction tab or a giver's view.</summary>
public readonly record struct BountyContractView(
    string Id, string Title, int Progress, int Required, int Reward, float Distance,
    bool Ready = false, string Giver = "", string Target = "", string Detail = "", string GiverName = "",
    BountyState State = BountyState.Accepted, double AcceptedAtGameHours = -1, double ExpiresAtGameHours = -1);

/// <summary>The published bounty board (built in the simulation's serial phase, read by the host).</summary>
public sealed record BountyBoardView(IReadOnlyList<BountyOfferView> Offers, IReadOnlyList<BountyContractView> Active)
{
    /// <summary>Open giver contracts for the journal; <see cref="Active"/> stays the global Faction-tab list.</summary>
    public IReadOnlyList<BountyContractView> Journal { get; init; } = [];

    public static BountyBoardView Empty { get; } = new([], []);
}

/// <summary>Accepting a board offer; the UI sends it through the normal command queue.</summary>
public sealed record AcceptBountyCommand(string ContractId) : SimCommand;

/// <summary>Giving up an accepted contract; the faction or target can be offered again.</summary>
public sealed record AbandonBountyCommand(string ContractId) : SimCommand;

/// <summary>Turning in a ready giver contract and taking its reward.</summary>
public sealed record TurnInBountyCommand(string ContractId) : SimCommand;

public sealed record BountySettings
{
    public int FactionKills { get; init; } = 3;
    public int FactionReward { get; init; } = 4500;
    public int AnimalKills { get; init; } = 3;
    public int AnimalReward { get; init; } = 3000;
    public int SpecificReward { get; init; } = 5000;
    public int CaptureReward { get; init; } = 6500;
    public int SpawnReward { get; init; } = 6000;
    /// <summary>Offer a hunt for one existing non-story bandit when one is near.</summary>
    public bool OfferSpecific { get; init; } = true;
    /// <summary>Offer a cull of one animal species when its members are near.</summary>
    public bool OfferAnimals { get; init; } = true;
    /// <summary>When no existing target is near, offer a target squad spawned on accept (needs a population).</summary>
    public bool OfferSpawned { get; init; } = true;
    /// <summary>Where a spawned target squad appears from the player (X/Z units).</summary>
    public float SpawnDistance { get; init; } = 1500;
    public float OfferRange { get; init; } = 20000;
    public int MaxOffers { get; init; } = 3;
    /// <summary>Ticks between looks for new offers.</summary>
    public int OfferRefreshTicks { get; init; } = 150;
    /// <summary>Ticks between rebuilt views (distance changes as the player moves).</summary>
    public int ViewRefreshTicks { get; init; } = 15;
    /// <summary>Contract definitions contributed by mods or scripts; null: only the built-in offers.</summary>
    public BountyContractCatalog? Contracts { get; init; }
}

/// <summary>
/// The contract board (the first player-facing task loop, docs/simulation.md "Crimes and bounties"). It offers faction-group kills, animal-species culls and a
/// hunt for one existing non-story bandit; a player killing blow through <see cref="Combat.ICombatDeathSink"/> advances the matching contract, and a specific
/// character target can also be completed by caging it (the Capture objective). Open and finished contracts are saved in the CAMERA record by
/// <see cref="Saving.BountyBoardSave"/>; offers are derived again from the world. Named targets carry a stable marker saved on the character
/// (<see cref="Saving.ContractTargetSave"/>), so an NPC that unloads with its platoon comes back as the same contract target.
/// </summary>
public sealed class BountyBoardSystem(PopulationData data, BountySettings? settings = null) : ITickSystem, IStateHashed, Combat.ICombatDeathSink
{
    readonly List<BountyOffer> offers = [];
    readonly List<BountyOffer> giverOffers = [];
    readonly Dictionary<string, IReadOnlyList<BountyOfferView>> publishedGiver = new(StringComparer.Ordinal);
    readonly List<BountyContract> contracts = [];
    readonly BountySettings settings = settings ?? new BountySettings();
    BountyBoardView published = BountyBoardView.Empty;

    public BountySettings Settings => settings;
    public IReadOnlyList<BountyContract> Contracts => contracts;
    public BountyBoardView Published => published;
    /// <summary>The population used to spawn a fallback contract target; null: no spawned offers.</summary>
    public PopulationSystem? Population { get; set; }

    public void Inputs(World world, IReadOnlyList<SimCommand> commands)
    {
        foreach (var command in commands)
        {
            if (command is AcceptBountyCommand accept) Accept(world, accept.ContractId);
            else if (command is AbandonBountyCommand abandon) Abandon(world, abandon.ContractId);
            else if (command is TurnInBountyCommand turnIn) TurnIn(world, turnIn.ContractId);
        }
    }

    public void SlowWorld(World world)
    {
        CheckOpenContracts(world);
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
        bool unique = world.Uniques.RecordOf(world.Characters.IdOf(defender)) is not null ||
            data.Db.Find(victim.RecordId) is { } victimRecord && UniqueCharacters.IsUnique(victimRecord);
        for (int i = 0; i < contracts.Count; i++)
        {
            var contract = contracts[i];
            if (contract.State != BountyState.Accepted || contract.Objective != BountyObjective.Kill) continue;
            bool matches = contract.Kind switch
            {
                BountyTargetKind.Faction => !unique && victim.Faction == contract.Faction,
                BountyTargetKind.AnimalSpecies => !unique && victim.Fighter is { IsAnimal: true } && victim.RecordId == contract.TargetRecord,
                BountyTargetKind.Character or BountyTargetKind.Spawned => victim.ContractTarget is { } marker && marker == contract.TargetMarker,
                _ => false,
            };
            if (!matches) continue;
            contract.Progress++;
            if (contract.Progress >= contract.Required) Finish(world, contract, attacker);
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
            if (contract.Objective == BountyObjective.Capture && target.IsImprisoned) Finish(world, contract, -1);
            else if (contract.Objective == BountyObjective.Kill && target.Medical is { Dead: true }) Finish(world, contract, -1);
        }
    }

    /// <summary>Checks giver death and accepted-contract deadlines before targets are refreshed.</summary>
    void CheckOpenContracts(World world)
    {
        for (int i = contracts.Count - 1; i >= 0; i--)
        {
            var contract = contracts[i];
            if (!contract.State.IsOpen()) continue;
            if (contract.GiverNpc.Length > 0 && TryParseCharacterId(contract.GiverNpc, out var giver) &&
                world.Characters.TryResolveNext(giver, out int slot) && world.Characters.Cold(slot)?.Medical is { Dead: true })
            {
                Fail(world, contract, "giver_dead");
                continue;
            }
            // A finished job stays ready for turn-in; only an unfinished one runs out of time.
            if (contract.State == BountyState.Accepted && contract.ExpiresAtGameHours >= 0 && world.GameHours >= contract.ExpiresAtGameHours)
                Expire(world, contract);
        }
    }

    void Fail(World world, BountyContract contract, string reason)
    {
        if (!contract.State.IsOpen()) return;
        if (contract.TargetMarker is { } marker) ClearMarker(world, marker);
        contract.State = BountyState.Failed;
        contract.FailureReason = reason;
        world.Post($"Bounty failed: {contract.Title}.");
        Publish(world);
    }

    void Expire(World world, BountyContract contract)
    {
        if (!contract.State.IsOpen()) return;
        if (contract.TargetMarker is { } marker) ClearMarker(world, marker);
        contract.State = BountyState.Expired;
        contract.FailureReason = "deadline";
        world.Post($"Bounty expired: {contract.Title}.");
        Publish(world);
    }

    static bool TryParseCharacterId(string text, out CharacterId id)
    {
        id = CharacterId.None;
        int dot = text.IndexOf('.');
        if (dot <= 0 || dot == text.Length - 1) return false;
        if (!int.TryParse(text.AsSpan(0, dot), out int slot) || !int.TryParse(text.AsSpan(dot + 1), out int generation)) return false;
        id = new CharacterId(slot, generation);
        return true;
    }

    void Finish(World world, BountyContract contract, int attacker)
    {
        if (contract.State != BountyState.Accepted) return;
        if (contract.Giver.Length > 0) MarkReady(world, contract);
        else Complete(world, contract, attacker);
    }

    void MarkReady(World world, BountyContract contract)
    {
        contract.Progress = contract.Required;
        contract.State = BountyState.ReadyToTurnIn;
        world.Post($"Bounty ready to turn in: {contract.Title}.");
        Publish(world);
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

    /// <summary>Turns in a ready giver contract and pays it. The public game-side API calls this from a dialogue choice.</summary>
    public bool TurnIn(World world, string id)
    {
        if (contracts.FirstOrDefault(c => c.Id == id && c.State == BountyState.ReadyToTurnIn) is not { } contract) return false;
        Complete(world, contract, -1);
        return true;
    }

    void Accept(World world, string id, string giverNpc = "", string giverName = "")
    {
        int at = offers.FindIndex(o => o.Id == id);
        bool giver = false;
        if (at < 0) { at = giverOffers.FindIndex(o => o.Id == id); giver = at >= 0; }
        if (at < 0 || contracts.Exists(c => c.Id == id)) return;
        var offer = giver ? giverOffers[at] : offers[at];
        string? spawnedName = null;
        if (offer.Kind == BountyTargetKind.Character)
        {
            if (!TryFindTarget(world, offer.Target, out _, out var target) || target.ContractTarget is not null || target.IsImprisoned || target.Medical is not { Dead: false }) return;
            SetMarker(target, offer.TargetMarker!);
        }
        else if (offer.Kind == BountyTargetKind.Spawned)
        {
            if (Population is null || offer.TargetRecord is not { } record || offer.TargetMarker is not { } marker) return;
            var players = PlayerPositions(world);
            if (players.Count == 0) return;
            float x = players[0].X + settings.SpawnDistance;
            var members = Population.SpawnSandboxSquad(world, record, new Vector2(x, players[0].Z),
                [Vector2.Zero, new(3, 0), new(6, 0)], 0, 0, offer.Faction);
            if (members.Count == 0 || !TryFindTarget(world, members[0], out _, out var target) || target.ContractTarget is not null) return;
            SetMarker(target, marker);
            spawnedName = target.Name;
        }
        if (giver) giverOffers.RemoveAt(at); else offers.RemoveAt(at);
        var contract = new BountyContract
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
            Giver = offer.Giver,
            GiverNpc = giver ? giverNpc : "",
            GiverName = giver ? (string.IsNullOrWhiteSpace(giverName) ? offer.Giver : giverName) : "",
            Detail = offer.Detail,
            Required = offer.Required,
            Reward = offer.Reward,
            AcceptedAtGameHours = world.GameHours,
            ExpiresAtGameHours = offer.DeadlineHours > 0 ? world.GameHours + offer.DeadlineHours : -1,
        };
        if (spawnedName is { Length: > 0 })
        {
            contract.Title = (contract.Objective == BountyObjective.Capture ? "Capture " : "Hunt ") + spawnedName;
            contract.TargetName = spawnedName;
        }
        contracts.Add(contract);
        world.Post($"Bounty accepted: {contract.Title}");
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

    /// <summary>Accepts an offer by ID from the interface; returns whether it found one.</summary>
    /// <param name="giverNpc">The giver character's stable id string, when a framework accepts a giver offer.</param>
    /// <param name="giverName">The giver's display name for the journal.</param>
    public bool TryAccept(World world, string id, string giverNpc = "", string giverName = "")
    {
        if (!offers.Exists(o => o.Id == id) && !giverOffers.Exists(o => o.Id == id)) return false;
        Accept(world, id, giverNpc, giverName);
        return contracts.Exists(c => c.Id == id);
    }

    public bool TryAbandon(World world, string id)
    {
        if (contracts.FirstOrDefault(c => c.Id == id && c.State is BountyState.Accepted or BountyState.ReadyToTurnIn) is not { } contract) return false;
        int at = contracts.IndexOf(contract);
        if (contract.TargetMarker is { } marker) ClearMarker(world, marker);
        contracts.RemoveAt(at);
        world.Post($"Bounty abandoned: {contract.Title}");
        Publish(world);
        return true;
    }

    /// <summary>Rebuilds offers after mod definitions changed; callable while paused.</summary>
    public void Refresh(World world)
    {
        RefreshOffers(world);
        Publish(world);
    }

    /// <summary>One giver's current offers, in the order the board built them.</summary>
    public IReadOnlyList<BountyOfferView> PublishedForGiver(string giver) =>
        publishedGiver.GetValueOrDefault(giver, []);

    void RefreshOffers(World world)
    {
        var players = PlayerPositions(world);
        offers.RemoveAll(o => float.IsPositiveInfinity(OfferDistance(world, o, players)));
        giverOffers.RemoveAll(o => o.Kind == BountyTargetKind.Character && float.IsPositiveInfinity(OfferDistance(world, o, players)));
        if (settings.Contracts is not { } catalog) giverOffers.Clear();
        else
        {
            var definitions = catalog.Definitions;
            var giverIds = definitions.Where(definition => definition.Giver.Length > 0).Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
            giverOffers.RemoveAll(o => !giverIds.Contains(o.Id) && !contracts.Exists(c => c.Id == o.Id));
            foreach (var definition in definitions)
            {
                if (definition.Giver.Length > 0)
                {
                    if (giverOffers.Exists(o => o.Id == definition.Id) || contracts.Exists(c => c.Id == definition.Id)) continue;
                    if (CustomOffer(definition, world, players, world.Seed) is { } giverOffer) giverOffers.Add(giverOffer);
                    continue;
                }
                if (offers.Count >= settings.MaxOffers) continue;
                if (offers.Exists(o => o.Id == definition.Id) || contracts.Exists(c => c.Id == definition.Id)) continue;
                if (CustomOffer(definition, world, players, world.Seed) is not { } offer || float.IsPositiveInfinity(OfferDistance(world, offer, players))) continue;
                offers.Add(offer);
            }
        }
        if (offers.Count < settings.MaxOffers && settings.OfferSpecific && !HasKind(BountyTargetKind.Character))
            if (FindSpecificTarget(world, players) is { } target) offers.Add(CharacterOffer(target, world.Seed));
        if (offers.Count < settings.MaxOffers && settings.OfferAnimals && !HasKind(BountyTargetKind.AnimalSpecies) && !HasContract(BountyTargetKind.AnimalSpecies))
            if (FindAnimalSpecies(world, players) is { } species) offers.Add(AnimalOffer(species, data));
        if (offers.Count < settings.MaxOffers)
        {
            var candidates = new List<(int Faction, float Distance)>();
            for (int f = 0; f < data.Factions.Count; f++)
            {
                var faction = data.Factions[f];
                if (faction.FundamentalType != FundamentalType.Bandit || faction.NotReal) continue;
                if (offers.Exists(o => o.Faction == f) || contracts.Exists(c => c.Giver.Length == 0 && c.Faction == f)) continue;
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
        if (offers.Count == 0 && settings.OfferSpawned && !HasKind(BountyTargetKind.Spawned) && !HasContract(BountyTargetKind.Spawned) && FindSpawnTarget(world, players) is { } spawn)
            offers.Add(SpawnOffer(spawn, world.Seed));
        Publish(world);
    }

    bool HasKind(BountyTargetKind kind) =>
        offers.Exists(o => o.Kind == kind) || contracts.Exists(c => c.Giver.Length == 0 && c.Kind == kind && c.State != BountyState.Completed);

    bool HasContract(BountyTargetKind kind) => contracts.Exists(c => c.Giver.Length == 0 && c.Kind == kind);

    BountyOffer FactionOffer(int faction) => new(
        data.Factions[faction].Id, BountyTargetKind.Faction, BountyObjective.Kill, data.Factions[faction].Name, data.Factions[faction].Name,
        $"{settings.FactionKills} kills", null, null, CharacterId.None, faction, data.Factions[faction].Id, settings.FactionKills, settings.FactionReward);

    BountyOffer AnimalOffer((string Record, int Count, float Distance) species, PopulationData db)
    {
        string name = db.Db.Find(species.Record)?.Name ?? species.Record;
        return new("cull:" + species.Record, BountyTargetKind.AnimalSpecies, BountyObjective.Kill, "Cull " + name, name,
            $"{settings.AnimalKills} animals", species.Record, null, CharacterId.None, -1, "", settings.AnimalKills, settings.AnimalReward);
    }

    BountyOffer SpawnOffer(SpawnCandidate spawn, ulong seed) => new(
        "spawn:" + spawn.FactionId, BountyTargetKind.Spawned, BountyObjective.Kill, "Hunt a " + spawn.FactionName, spawn.FactionName,
        "Kill the target", spawn.RecordId, $"spawn:{seed}:{spawn.FactionId}", CharacterId.None, spawn.Faction, spawn.FactionId, 1, settings.SpawnReward);

    BountyOffer? CustomOffer(BountyContractDefinition definition, World world, List<Vector3> players, ulong seed)
    {
        string detail = !string.IsNullOrEmpty(definition.Detail)
            ? definition.Detail
            : definition.Kind switch
            {
                BountyTargetKind.Faction => $"{definition.Required} kills",
                BountyTargetKind.AnimalSpecies => $"{definition.Required} animals",
                BountyTargetKind.Spawned or BountyTargetKind.Character => definition.Objective == BountyObjective.Capture ? "Cage the target" : "Kill the target",
                _ => "",
            };
        switch (definition.Kind)
        {
            case BountyTargetKind.Faction:
            {
                int faction = data.FactionIndex(definition.FactionId);
                if (faction < 0) return null;
                return new(definition.Id, definition.Kind, definition.Objective, definition.Title, data.Factions[faction].Name, detail,
                    null, null, CharacterId.None, faction, data.Factions[faction].Id, definition.Required, definition.Reward, definition.Giver, definition.DeadlineHours);
            }
            case BountyTargetKind.AnimalSpecies:
            {
                if (data.Db.Find(definition.TargetRecord) is not { Type: FcsRecordType.ANIMAL_CHARACTER } record || UniqueCharacters.IsUnique(record))
                    return null;
                return new(definition.Id, definition.Kind, definition.Objective, definition.Title, record.Name, detail,
                    definition.TargetRecord, null, CharacterId.None, -1, "", definition.Required, definition.Reward, definition.Giver, definition.DeadlineHours);
            }
            case BountyTargetKind.Character:
            {
                if (FindContentCharacterTarget(world, players, definition, definition.Objective == BountyObjective.Capture) is not { } target) return null;
                int faction = target.Cold.Faction;
                string factionId = (uint)faction < (uint)data.Factions.Count ? data.Factions[faction].Id : "";
                string marker = $"hunt:{seed}:{definition.Id}:{target.Id.Slot}:{target.Id.Generation}";
                return new(definition.Id, definition.Kind, definition.Objective, definition.Title, target.Cold.Name, detail,
                    target.Cold.RecordId, marker, target.Id, faction, factionId, definition.Required, definition.Reward, definition.Giver, definition.DeadlineHours);
            }
            case BountyTargetKind.Spawned:
            {
                if (Population is null || data.Db.Find(definition.TargetRecord) is not { Type: FcsRecordType.CHARACTER } record ||
                    UniqueCharacters.IsUnique(record) || data.FactionIndex(definition.FactionId) is not (>= 0 and var faction))
                    return null;
                return new(definition.Id, definition.Kind, definition.Objective, definition.Title, data.Factions[faction].Name, detail,
                    definition.TargetRecord, $"spawn:{seed}:{definition.Id}", CharacterId.None, faction, data.Factions[faction].Id, definition.Required, definition.Reward, definition.Giver, definition.DeadlineHours);
            }
            default:
                return null;
        }
    }

    (CharacterId Id, CharacterCold Cold, float Distance)? FindContentCharacterTarget(World world, List<Vector3> players, BountyContractDefinition definition, bool capture)
    {
        if (players.Count == 0) return null;
        string? record = string.IsNullOrWhiteSpace(definition.TargetRecord) ? null : definition.TargetRecord;
        if (record is not null &&
            (data.Db.Find(record) is not { Type: FcsRecordType.CHARACTER } foundRecord || UniqueCharacters.IsUnique(foundRecord)))
            return null;
        int faction = -1;
        if (!string.IsNullOrWhiteSpace(definition.FactionId))
        {
            faction = data.FactionIndex(definition.FactionId);
            if (faction < 0) return null;
        }

        (CharacterId Id, CharacterCold Cold, float Distance)? best = null;
        for (int s = 0; s < world.Characters.HighWater; s++)
        {
            if (!world.Characters.Next[s].Alive) continue;
            var id = world.Characters.IdOf(s);
            if (world.Characters.Cold(s) is not
                { IsPlayer: false, Medical.Dead: false, IsImprisoned: false, CarriedBy.IsNone: true, ContractTarget: null } cold)
                continue;
            if (cold.Fighter is { IsAnimal: true }) continue;
            if (capture && cold.Medical is not { Incapacitated: true }) continue;
            if (record is not null && cold.RecordId != record) continue;
            if (faction >= 0 && cold.Faction != faction) continue;
            if (offers.Exists(offer => offer.Target == id) || giverOffers.Exists(offer => offer.Target == id)) continue;
            if (world.Uniques.RecordOf(id) is not null ||
                data.Db.Find(cold.RecordId) is { } recordData && UniqueCharacters.IsUnique(recordData))
                continue;
            float distance = Distance(players, world.Characters.Next[s].Position);
            if (distance > settings.OfferRange || best is { } current && current.Distance <= distance) continue;
            best = (id, cold, distance);
        }
        return best;
    }

    SpawnCandidate? FindSpawnTarget(World world, List<Vector3> players)
    {
        if (Population is null || players.Count == 0) return null;
        for (int f = 0; f < data.Factions.Count; f++)
        {
            var faction = data.Factions[f];
            if (faction.FundamentalType != FundamentalType.Bandit || faction.NotReal) continue;
            foreach (var link in faction.Squads.Concat(faction.SquadDefault).Concat(faction.SpecialSquads).Concat(faction.Residents))
            {
                if (data.Db.Find(link.Id) is not { } record || !SquadFactory.IsTemplate(record)) continue;
                var template = SquadTemplate.From(record);
                var plan = SquadFactory.Plan(data.Db, template, 1, world.Seed,
                    Rng.Hash(world.Seed, Rng.StableHash(template.Id), RngPurpose.Spawn), data.States);
                var member = plan.Members.FirstOrDefault(m => !m.IsAnimal && data.Db.Find(m.RecordId) is { } r && !Meitou.Simulation.UniqueCharacters.IsUnique(r));
                if (member is not null) return new SpawnCandidate(f, faction.Id, faction.Name, member.RecordId);
            }
        }
        return null;
    }

    readonly record struct SpawnCandidate(int Faction, string FactionId, string FactionName, string RecordId);

    BountyOffer CharacterOffer((CharacterId Id, CharacterCold Cold, float Distance) target, ulong seed)
    {
        string marker = $"hunt:{seed}:{target.Id.Slot}:{target.Id.Generation}";
        int faction = target.Cold.Faction;
        string factionId = (uint)faction < (uint)data.Factions.Count ? data.Factions[faction].Id : "";
        bool capture = target.Cold.Medical is { Incapacitated: true };
        string verb = capture ? "Capture " : "Hunt ";
        string detail = capture ? "Cage the target" : "Kill the target";
        int reward = capture ? settings.CaptureReward : settings.SpecificReward;
        return new(marker, BountyTargetKind.Character, capture ? BountyObjective.Capture : BountyObjective.Kill, verb + target.Cold.Name, target.Cold.Name, detail,
            target.Cold.RecordId, marker, target.Id, faction, factionId, 1, reward);
    }

    void Publish(World world)
    {
        var players = PlayerPositions(world);
        var viewOffers = new List<BountyOfferView>(offers.Count);
        foreach (var offer in offers)
            viewOffers.Add(new BountyOfferView(offer.Id, offer.Title, offer.Detail, offer.Required, offer.Reward, OfferDistance(world, offer, players), offer.DeadlineHours));
        var active = new List<BountyContractView>();
        var journal = new List<BountyContractView>();
        foreach (var contract in contracts)
        {
            if (!contract.State.IsOpen()) continue;
            var view = new BountyContractView(contract.Id, contract.Title, contract.Progress, contract.Required, contract.Reward,
                ContractDistance(world, contract, players), contract.State == BountyState.ReadyToTurnIn, contract.Giver,
                contract.TargetName, contract.Detail, contract.GiverName, contract.State, contract.AcceptedAtGameHours, contract.ExpiresAtGameHours);
            if (contract.Giver.Length == 0) active.Add(view);
            else journal.Add(view);
        }
        published = new BountyBoardView(viewOffers, active) { Journal = journal };
        publishedGiver.Clear();
        foreach (var group in giverOffers.GroupBy(offer => offer.Giver, StringComparer.Ordinal))
            publishedGiver[group.Key] = [.. group.Select(offer => new BountyOfferView(offer.Id, offer.Title, offer.Detail, offer.Required, offer.Reward,
                OfferDistance(world, offer, players), offer.DeadlineHours))];
    }

    float OfferDistance(World world, BountyOffer offer, List<Vector3> players) => offer.Kind switch
    {
        BountyTargetKind.Faction => NearestFaction(world, offer.Faction, players),
        BountyTargetKind.AnimalSpecies => NearestAnimal(world, offer.TargetRecord, players),
        BountyTargetKind.Character => NearestCharacter(world, offer.Target, players),
        BountyTargetKind.Spawned => players.Count == 0 ? float.PositiveInfinity : settings.SpawnDistance,
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

    float NearestFaction(World world, int faction, List<Vector3> players)
    {
        if (players.Count == 0) return float.PositiveInfinity;
        float best = float.PositiveInfinity;
        for (int s = 0; s < world.Characters.HighWater; s++)
        {
            if (!world.Characters.Next[s].Alive || world.Characters.Cold(s) is not { Medical.Dead: false } c || c.Faction != faction || c.IsPlayer) continue;
            if (world.Uniques.RecordOf(world.Characters.IdOf(s)) is not null ||
                data.Db.Find(c.RecordId) is { } record && UniqueCharacters.IsUnique(record)) continue;
            best = MathF.Min(best, Distance(players, world.Characters.Next[s].Position));
        }
        return best;
    }

    float NearestAnimal(World world, string? record, List<Vector3> players)
    {
        if (players.Count == 0 || record is null) return float.PositiveInfinity;
        float best = float.PositiveInfinity;
        for (int s = 0; s < world.Characters.HighWater; s++)
        {
            if (!world.Characters.Next[s].Alive || world.Characters.Cold(s) is not { Medical.Dead: false, Fighter.IsAnimal: true } c || c.RecordId != record) continue;
            if (world.Uniques.RecordOf(world.Characters.IdOf(s)) is not null ||
                data.Db.Find(c.RecordId) is { } animal && UniqueCharacters.IsUnique(animal)) continue;
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
        if (players.Count == 0 || id.IsNone || !TryFindTarget(world, id, out int slot, out var target) ||
            target.Medical is not { Dead: false } || target.IsImprisoned || target.ContractTarget is not null)
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
            if (offers.Exists(offer => offer.Target == id) || giverOffers.Exists(offer => offer.Target == id)) continue;
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
            if (world.Uniques.RecordOf(world.Characters.IdOf(s)) is not null ||
                data.Db.Find(c.RecordId) is { } animal && UniqueCharacters.IsUnique(animal)) continue;
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
        CharacterId target, int faction, string factionId, int required, int reward, string giver = "", int deadlineHours = 0)
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
        public string Giver { get; } = giver;
        public int DeadlineHours { get; } = deadlineHours;
    }

    /// <summary>Restores one saved contract; called by <see cref="Saving.BountyBoardSave"/> on load.</summary>
    public void Restore(string id, BountyTargetKind kind, BountyObjective objective, string title, string targetName, string? targetRecord, string? targetMarker,
        int faction, int required, int progress, int reward, BountyState state, string giver = "", string detail = "", string giverName = "",
        string giverNpc = "", double acceptedAtGameHours = -1, double expiresAtGameHours = -1, string failureReason = "")
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
            Giver = giver,
            GiverName = giverName,
            GiverNpc = giverNpc,
            Detail = detail,
            Required = required,
            Reward = Math.Max(0, reward),
            Progress = Math.Clamp(progress, 0, required),
            State = state,
            AcceptedAtGameHours = acceptedAtGameHours,
            ExpiresAtGameHours = expiresAtGameHours,
            FailureReason = failureReason,
            CompletedTick = state == BountyState.Completed ? 0 : -1,
        });
    }

    /// <summary>Rebuilds the display after a load, before the first tick.</summary>
    public void PublishForLoad(World world) => Publish(world);
}
