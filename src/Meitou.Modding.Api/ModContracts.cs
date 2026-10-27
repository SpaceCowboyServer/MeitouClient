using Meitou.Data;
using Meitou.Simulation;

namespace Meitou.Modding.Api;

/// <summary>The entry type named by a <c>trust: plugin</c> manifest's <c>entry</c> field.</summary>
public interface IMod
{
    void Initialize(IModContext context);
}

/// <summary>The stable surface a C# mod receives after the data load and merge, before the first world is built.</summary>
public interface IModContext
{
    /// <summary>The manifest's stable ID.</summary>
    string Id { get; }
    string Version { get; }
    string Folder { get; }
    /// <summary>The modding contract version this game build implements.</summary>
    int ApiVersion { get; }
    /// <summary>The merged game database, the same instance the next world is built from.</summary>
    GameDatabase Data { get; }
    /// <summary>A stable salt for the mod's random streams; mix it into <see cref="Rng.Hash"/> keys.</summary>
    ulong RandomSalt { get; }
    IModLog Log { get; }
    IModServices Services { get; }

    /// <summary>
    /// Typed notifications from the simulation. Handlers run on the simulation thread; registration order is preserved. A throwing handler is
    /// isolated from the other handlers and from the game.
    /// </summary>
    IModEvents Events { get; }

    /// <summary>The game's shared bounty contract registry. Definitions registered here are offered by the Faction tab board.</summary>
    IBountyContracts Bounties { get; }

    /// <summary>A read-only view of the board's current offers and accepted contracts.</summary>
    IBountyBoard Board { get; }

    /// <summary>Adds dynamic conversation options for NPCs such as bartenders.</summary>
    IDialogue Dialogue { get; }

    /// <summary>
    /// Registers a per-world system factory. Systems are appended after the standard systems, in mod load order and registration order; the
    /// factory runs once for each new world, so systems must not be shared between worlds. This is a trusted-plugin escape hatch, not part of
    /// the stable content contract.
    /// </summary>
    void AddSystem(Func<ITickSystem> factory);

    /// <summary>Registers state written to this mod's sidecar in every save and read back before the next world is built.</summary>
    void AddSaveable(ISaveable saveable);

    /// <summary>
    /// Queues a command for the simulation. Before the first world exists it is held and queued as soon as a world is attached; afterwards it
    /// goes straight to <see cref="World.Commands"/>.
    /// </summary>
    void Enqueue(SimCommand command);
}

/// <summary>
/// The host-side API services passed to the C# and Lua hosts. It is not the surface a mod sees: <see cref="IModContext"/> exposes the
/// scoped services and the event bus.
/// </summary>
public interface IModApiHost
{
    IModEvents Events { get; }
    IBountyBoard Board { get; }
    IDialogue Dialogue { get; }
    /// <summary>Returns a registry that prefixes local IDs with <paramref name="modId"/>.</summary>
    IBountyContracts BountiesFor(string modId);
    /// <summary>Removes every definition a mod registered; used when its initialization fails.</summary>
    void RemoveBounties(string modId);
}

/// <summary>Typed event subscriptions. One event bus is shared by the C# and Lua hosts.</summary>
public interface IModEvents
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);
}

/// <summary>The first tick of a world. Seed is the world's stable seed.</summary>
public sealed record WorldStarted(ulong Seed, long Tick, long Money, double GameHours);

/// <summary>One simulation tick, after the bounty board has processed it.</summary>
public sealed record WorldTick(ulong Seed, long Tick, long Money, double GameHours);

/// <summary>The game clock crossed into a new day (day = floor(game hours / 24)).</summary>
public sealed record WorldDayChanged(int Day, long Tick, double GameHours);

/// <summary>A contract that became accepted. The string fields use the same values as the Lua payload.</summary>
public sealed record BountyAccepted(
    string Id, string Title, string Target, string Kind, string Objective,
    int Required, int Reward, int Progress, string Giver = "", string GiverName = "");

/// <summary>A giver contract whose objective is done and whose reward waits for turn-in.</summary>
public sealed record BountyReady(
    string Id, string Title, string Target, string Kind, string Objective,
    int Required, int Reward, string Giver = "", string GiverName = "");

/// <summary>A contract that completed and paid its reward.</summary>
public sealed record BountyCompleted(
    string Id, string Title, string Target, string Kind, string Objective,
    int Required, int Reward, string Giver = "", string GiverName = "");

/// <summary>A contract that failed; <see cref="Reason"/> is a stable code such as <c>giver_dead</c>.</summary>
public sealed record BountyFailed(
    string Id, string Title, string Target, string Kind, string Objective,
    int Required, int Reward, string Reason, string Giver = "", string GiverName = "");

/// <summary>A contract that ran out of time; <see cref="Reason"/> is <c>deadline</c>.</summary>
public sealed record BountyExpired(
    string Id, string Title, string Target, string Kind, string Objective,
    int Required, int Reward, string Reason, string Giver = "", string GiverName = "");

/// <summary>What a mod contract targets. Existing characters are not content contracts.</summary>
public enum BountyTarget { Faction, AnimalSpecies, Spawned }

/// <summary>What the player must do to the target.</summary>
public enum BountyGoal { Kill, Capture }

/// <summary>
/// A content contract. <see cref="LocalId"/> is namespaced by the registering mod; the returned canonical ID is
/// <c>&lt;mod id&gt;:bounty/&lt;local id&gt;</c>. Faction and record fields are FCS string IDs.
/// </summary>
public sealed record BountyDefinition(
    string LocalId,
    BountyTarget Target,
    BountyGoal Goal,
    string Title,
    string Detail = "",
    string TargetRecord = "",
    string FactionId = "",
    int Required = 1,
    int Reward = 0,
    string Giver = "",
    int DeadlineHours = 0);

/// <summary>
/// A reusable contract template offered through a framework mod. The framework clones <see cref="Definition"/> for each giver and
/// assigns the actual giver key and instance ID.
/// </summary>
public sealed record ContractTemplate(
    BountyDefinition Definition,
    string GiverRole = "bartender",
    string TownId = "",
    string TownName = "");

/// <summary>Implemented by a reusable contract-giver framework; content mods register templates through it.</summary>
public interface IContractFramework
{
    /// <summary>Registers a template for givers matching its role and optional town filter. Template IDs must be unique.</summary>
    void RegisterTemplate(ContractTemplate template);
}

/// <summary>The lifecycle state of one accepted contract.</summary>
public enum BountyStatus { Accepted, ReadyToTurnIn, Completed, Failed, Expired }

/// <summary>One offer in the Faction tab or a giver's conversation.</summary>
public sealed record BountyOffer(
    string Id, string Title, string Detail, int Required, int Reward, float Distance, int DeadlineHours = 0);

/// <summary>One accepted contract in the Faction tab, a giver conversation or the journal.</summary>
public sealed record BountyContractView(
    string Id, string Title, int Progress, int Required, int Reward, float Distance, bool Ready = false, string Giver = "",
    string Target = "", string Detail = "", string GiverName = "", BountyStatus Status = BountyStatus.Accepted,
    double AcceptedAtGameHours = -1, double ExpiresAtGameHours = -1);

/// <summary>Registers content contracts into the game's shared board.</summary>
public interface IBountyContracts
{
    /// <summary>Registers a local ID and returns the canonical <c>&lt;mod id&gt;:bounty/&lt;local id&gt;</c>.</summary>
    string Register(BountyDefinition definition);

    /// <summary>Removes a definition owned by this mod; offers already accepted stay on the board.</summary>
    bool Remove(string id);
}

/// <summary>A read-only view of the board; the lists are snapshots. Giver methods return the contracts offered by one bartender.</summary>
public interface IBountyBoard
{
    IReadOnlyList<BountyOffer> Offers { get; }
    IReadOnlyList<BountyContractView> Active { get; }
    /// <summary>Open giver contracts, for the contract journal.</summary>
    IReadOnlyList<BountyContractView> Journal { get; }
    IReadOnlyList<BountyOffer> OffersFor(string giver);
    IReadOnlyList<BountyContractView> ActiveFor(string giver);
    IReadOnlyList<BountyContractView> CompletedFor(string giver);
    /// <summary>
    /// Accept one giver offer. Applies immediately so the open conversation can refresh its choices. A framework passes the giver's
    /// stable character id and name so giver death and the journal can be tracked.
    /// </summary>
    bool Accept(string offerId, string giverNpc = "", string giverName = "");
    /// <summary>Turn in a ready giver contract and pay its reward.</summary>
    bool TurnIn(string contractId);
    bool Abandon(string contractId);
    /// <summary>Rebuilds giver offers after definitions changed; safe while the game is paused.</summary>
    void Refresh();
}

/// <summary>A dynamic dialogue choice for an NPC. Return null text to hide it.</summary>
public sealed record DialogueOption(
    string Id,
    Func<DialogueContext, string?> Text,
    Func<DialogueContext, bool>? CanShow = null,
    Action<DialogueContext>? Selected = null);

/// <summary>The NPC and place a custom dialogue option is offered for.</summary>
public sealed record DialogueContext(
    string NpcId, string NpcName, string NpcRecord, string TownId, string TownName,
    int Day, bool IsTrader, bool IsBartender, bool AtRoot)
{
    /// <summary>A stable key for a bartender's contract list (town and character record).</summary>
    public string GiverId => (TownId.Length > 0 ? TownId : "unknown") + ":" + NpcRecord;
}

/// <summary>Registers dynamic conversation options shared by the C# and Lua hosts.</summary>
public interface IDialogue
{
    IDisposable AddOption(DialogueOption option);
}

/// <summary>Stable service keys shared by Meitou mods and the game. Use these instead of spelling a key in several mods.</summary>
public static class ModServiceKeys
{
    /// <summary>The reusable contract-giver framework service.</summary>
    public const string Contracts = "frontier.contracts";

    /// <summary>
    /// Legacy key for a raw <c>Meitou.Simulation.Bounties.BountyContractCatalog</c>. The game now owns the shared catalog and exposes it through
    /// <see cref="IModContext.Bounties"/>; new code should not provide a catalog under this key.
    /// </summary>
    public const string BountyContracts = "frontier.bounties.contracts";
}

public interface IModLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

/// <summary>
/// A small, string-keyed registry shared by the loaded plugins. Earlier mods register first; a duplicate key is an error. Typed contracts
/// work when both sides reference the same contract assembly (the usual case for a framework and its content packs).
/// </summary>
public interface IModServices
{
    void Provide(string key, object service);
    bool TryGet(string key, out object? service);

    T? Get<T>(string key) where T : class => TryGet(key, out var service) ? service as T : null;
}

/// <summary>
/// One piece of a mod's save state. The key is stable across versions and unique within the mod; the host writes all saveables of a mod into
/// one <c>meitou-mods/&lt;id&gt;.bin</c> sidecar in the save folder, which the original game ignores.
/// </summary>
public interface ISaveable
{
    string Key { get; }
    void Write(Stream stream);
    void Read(Stream stream);
}
