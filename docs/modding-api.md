# Generic mod API

**Status: implemented (2026-10-27).** C# plugins and Lua scripts share one event bus and one bounty registry in
`Meitou.Modding.Runtime`. The bounty samples use this contract; the legacy `BountyContractCatalog` service key remains only for
source compatibility and is not read by the game.

```text
C# plugin ──┐
            ├── IModEvents / IBountyContracts / IBountyBoard / IDialogue ── game adapters ── simulation
Lua script ─┘
```

## Rules

- Public signatures use API DTOs and primitive values, never `World`, `GameDatabase`, `FcsRecord` or a simulation system.
- C# and Lua share event names, payload fields and registration rules; they do not share CLR types.
- The stable APIs are opt-in. `Data`, `AddSystem`, `AddSaveable` and `Enqueue` remain trusted-plugin escape hatches.
- `Meitou.Simulation` does not reference `Meitou.Modding.Api`; `Meitou.Modding.Runtime` owns the translation.
- Events are read-only notifications on the simulation thread. A throwing handler is logged and does not stop the other handlers,
  the board or the world.

## C# contract

`IModContext` (in `Meitou.Modding.Api`) exposes:

```csharp
IModEvents Events { get; }
IBountyContracts Bounties { get; }
IBountyBoard Board { get; }
IDialogue Dialogue { get; }
```

```csharp
public interface IModEvents
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);
}

public sealed record WorldStarted(ulong Seed, long Tick, long Money, double GameHours);
public sealed record WorldTick(ulong Seed, long Tick, long Money, double GameHours);
public sealed record WorldDayChanged(int Day, long Tick, double GameHours);

public sealed record BountyAccepted(
    string Id, string Title, string Target, string Kind, string Objective,
    int Required, int Reward, int Progress, string Giver = "", string GiverName = "");

public sealed record BountyReady(
    string Id, string Title, string Target, string Kind, string Objective,
    int Required, int Reward, string Giver = "", string GiverName = "");

public sealed record BountyCompleted(
    string Id, string Title, string Target, string Kind, string Objective,
    int Required, int Reward, string Giver = "", string GiverName = "");

public sealed record BountyFailed(
    string Id, string Title, string Target, string Kind, string Objective,
    int Required, int Reward, string Reason, string Giver = "", string GiverName = "");

public sealed record BountyExpired(
    string Id, string Title, string Target, string Kind, string Objective,
    int Required, int Reward, string Reason, string Giver = "", string GiverName = "");

public enum BountyTarget { Faction, AnimalSpecies, Spawned, Character }
public enum BountyGoal { Kill, Capture }

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

public sealed record ContractTemplate(
    BountyDefinition Definition,
    string GiverRole = "bartender",
    string TownId = "",
    string TownName = "");

public interface IContractFramework
{
    void RegisterTemplate(ContractTemplate template);
}

public static class ModServiceKeys
{
    public const string Contracts = "frontier.contracts";
}

public enum BountyStatus { Accepted, ReadyToTurnIn, Completed, Failed, Expired }

public sealed record BountyOffer(
    string Id, string Title, string Detail, int Required, int Reward, float Distance, int DeadlineHours = 0);

public sealed record BountyContractView(
    string Id, string Title, int Progress, int Required, int Reward, float Distance,
    bool Ready = false, string Giver = "", string Target = "", string Detail = "", string GiverName = "",
    BountyStatus Status = BountyStatus.Accepted, double AcceptedAtGameHours = -1, double ExpiresAtGameHours = -1);

public interface IBountyContracts
{
    string Register(BountyDefinition definition);
    bool Remove(string id);
}

public interface IBountyBoard
{
    IReadOnlyList<BountyOffer> Offers { get; }
    IReadOnlyList<BountyContractView> Active { get; }
    IReadOnlyList<BountyContractView> Journal { get; }
    IReadOnlyList<BountyOffer> OffersFor(string giver);
    IReadOnlyList<BountyContractView> ActiveFor(string giver);
    IReadOnlyList<BountyContractView> CompletedFor(string giver);
    bool Accept(string offerId, string giverNpc = "", string giverName = "");
    bool TurnIn(string contractId);
    bool Abandon(string contractId);
    void Refresh();
}

public sealed record DialogueContext(
    string NpcId, string NpcName, string NpcRecord, string TownId, string TownName,
    int Day, bool IsTrader, bool IsBartender, bool AtRoot)
{
    public string GiverId => (TownId.Length > 0 ? TownId : "unknown") + ":" + NpcRecord;
}

public sealed record DialogueOption(
    string Id,
    Func<DialogueContext, string?> Text,
    Func<DialogueContext, bool>? CanShow = null,
    Action<DialogueContext>? Selected = null);

public interface IDialogue
{
    IDisposable AddOption(DialogueOption option);
}
```

- `LocalId` uses 1-64 letters, digits, `_`, `-` or `.`; registration returns `<mod id>:bounty/<local id>`.
- `FactionId` and `TargetRecord` are FCS string IDs. `Required` defaults to 1 and `Reward` to 0. `DeadlineHours` defaults to
  0 (no deadline) and is capped at 10,000.
- `BountyTarget.Character` picks a nearby non-unique character at offer time. Use `FactionId`, `TargetRecord`, or both;
  `Required` must be 1. The offer carries the resolved name and marker, and the contract follows that character.
- `Giver` is a bartender key. A non-empty giver hides the offer from the Faction tab and exposes it through
  `OffersFor`/`ActiveFor`; an empty giver is a normal global contract. `Journal` lists open giver contracts after the player
  walks away from the bar.
- `DeadlineHours` is optional (0 means no deadline). An unfinished accepted contract expires at `AcceptedAtGameHours +
  DeadlineHours`; a ready contract stays ready for turn-in. Giver death fails a contract only while the giver character is loaded.
- `BountyStatus` and the detail fields expose accepted, ready, completed, failed and expired states to mods. `BountyFailed.Reason`
  is `giver_dead`; `BountyExpired.Reason` is `deadline`.
- `ContractTemplate` and `IContractFramework` are the cross-mod framework contract. The foundation plugin
  [frontier.contracts](../sdk/samples/frontier.contracts) provides `ModServiceKeys.Contracts`; content packs depend on the
  `frontier.contracts` capability and register templates with `context.Services.Get<IContractFramework>(...)`.
  Template IDs are globally unique; `GiverRole` defaults to `bartender`, and a town filter is optional.
- Event `Target` is the display name; `Kind` is `faction`, `animal`, `spawned` or `character`; `Objective` is `kill` or `capture`.
- `Capture` is accepted only for `Spawned` contracts; built-in downed named targets already use the capture path.
- A giver contract with progress at its required count becomes `Ready`; its reward is paid by `TurnIn`, not on the killing blow.
- `Board` is a snapshot of the current published global board. The lists are empty before the first board publish.
- Handler registration order is invocation order. A subscription is removed with `Dispose`.
- `Dialogue.AddOption` appends dynamic choices to a conversation. `AtRoot` is true on the first page (before a player reply);
  `IsBartender` is true for a trader in a bar or a bar-squad trader. `GiverId` is the stable town/record key for a bartender's list.

## Lua mapping

| Lua | C# event | Payload |
|---|---|---|
| `world.start` | `WorldStarted` | `tick`, `seed` (hex), `money`, `game_hours` |
| `world.tick` | `WorldTick` | `tick`, `seed` (hex), `money`, `game_hours` |
| `day.changed` | `WorldDayChanged` | `day`, `tick`, `game_hours` |
| `bounty.accepted` | `BountyAccepted` | `id`, `title`, `target`, `kind`, `objective`, `required`, `reward`, `progress`, `giver`, `giver_name` |
| `bounty.ready` | `BountyReady` | same fields; objective complete, waiting for turn-in |
| `bounty.completed` | `BountyCompleted` | same fields; `progress` equals `required` |
| `bounty.failed` | `BountyFailed` | same fields plus `reason` (`giver_dead`) |
| `bounty.expired` | `BountyExpired` | same fields plus `reason` (`deadline`) |

`meitou.contracts.register` maps onto `IBountyContracts.Register`; it accepts an optional `giver` key. `meitou.events.on`
subscribes to the shared bus. A Lua handler that throws disables only that script and removes its contract definitions. Contracts
restored from a save at the first tick are recorded without replaying `bounty.accepted`; later accepts and completions fire
normally. The dynamic `IDialogue` option hook is C#-only in this slice; Lua gets the day and bounty events.

## Runtime and lifecycle

- `GameHost` creates one `ModApiRuntime` before loading the C# and Lua hosts. It owns one `ModEventBus` and one
  `BountyContractCatalog`.
- `ModPluginHost` gives each plugin a scoped `IBountyContracts`; `LuaModHost` gives each script the same scoped view. A failed
  initialization removes that mod's definitions.
- `ModRuntimeSystem` is appended after the standard systems and publishes `WorldStarted` once, then board changes and
  `WorldTick` on every simulation tick. It records the board's first state without synthetic accepted events, publishes
  `WorldDayChanged` when the game clock crosses a day, and attaches `ModDialogue` to the dialogue system.
- The game's `BountySettings.Contracts` is the same catalog, so plugin, script and built-in offers share one board. Giver offers live
  in their own list and are built by `Refresh` after a mod changes definitions.
- There is no hot reload or unload. Event subscriptions live for the game session.

## Permissions

Script manifests may declare an optional permission list:

```json
"permissions": ["events:world", "events:bounty", "contracts:write"]
```

| Permission | Allows |
|---|---|
| `events:world` | `world.start`, `world.tick`, `day.changed` |
| `events:bounty` | `bounty.accepted`, `bounty.completed` |
| `contracts:write` | `meitou.contracts.register` |

- Unknown or duplicate permissions are manifest errors.
- If `permissions` is absent or empty, API 1 keeps the legacy permissive behavior for these three capabilities.
- If present and non-empty, only the listed capabilities are available; a denied call fails during script initialization and the
  script is disabled. C# plugins are trusted and are not gated by this list.

The build remains `apiVersion: 1`. Domain capability names are `frontier.contracts@1`, `frontier.bounty.events@1` and
`frontier.world.events@1`; a mod that provides a capability to other mods advertises it in `provides`.

## Migration

- `ModServiceKeys.BountyContracts` remains only as a legacy key. The game does not read a plugin-provided catalog; use
  `context.Bounties`.
- Lua no longer polls the board from a per-world system; event delivery is through `IModEvents`.
- Existing accepted/completed contract saves keep their CAMERA state; the first tick suppresses accepted replays. Giver keys and the ready
  state are additive CAMERA keys; old saves read as global contracts. Deadlines, failure reasons, giver names/character ids and details are
  additive keys; missing values read as no deadline and an empty name/reason.
- The examples are [frontier.contracts](../sdk/samples/frontier.contracts) (foundation),
  [frontier.bounties](../sdk/samples/frontier.bounties) (content pack) and
  [frontier.bounty.dust_bandit](../sdk/samples/frontier.bounty.dust_bandit) (Lua global contract/event demo).
- `frontier.contracts@1` is the reusable capability. The framework owns giver slots and dialogue; the engine still owns
  accepted/ready/completed contract state and save data. Lua content can register raw engine contracts; Lua access to the
  framework template service is future work.

## Not implemented

- Commands, scheduler, factions, economy, save-state and asset APIs.
- Lua dynamic dialogue options (C# has them now).
- UI/rendering hooks, hot reload, Workshop publishing and remote repositories.
- Removing the trusted C# escape hatch (an API 2 decision).

See [contracts-design](contracts-design.md) for the Anomaly-derived lifecycle/giver backlog (failure, expiry, templates,
journal and generated targets).
