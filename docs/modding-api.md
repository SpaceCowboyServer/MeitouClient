# Generic mod API

**Status: implemented (2026-10-27).** C# plugins and Lua scripts share one event bus and one bounty registry in
`Meitou.Modding.Runtime`. The bounty samples use this contract; the legacy `BountyContractCatalog` service key remains only for
source compatibility and is not read by the game.

```text
C# plugin ──┐
            ├── IModEvents / IBountyContracts / IBountyBoard ── game adapters ── simulation
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
```

```csharp
public interface IModEvents
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);
}

public sealed record WorldStarted(ulong Seed, long Tick, long Money, double GameHours);
public sealed record WorldTick(ulong Seed, long Tick, long Money, double GameHours);

public sealed record BountyAccepted(
    string Id, string Title, string Target, string Kind, string Objective,
    int Required, int Reward, int Progress);

public sealed record BountyCompleted(
    string Id, string Title, string Target, string Kind, string Objective,
    int Required, int Reward);

public enum BountyTarget { Faction, AnimalSpecies, Spawned }
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
    int Reward = 0);

public sealed record BountyOffer(
    string Id, string Title, string Detail, int Required, int Reward, float Distance);

public sealed record BountyContractView(
    string Id, string Title, int Progress, int Required, int Reward, float Distance);

public interface IBountyContracts
{
    string Register(BountyDefinition definition);
}

public interface IBountyBoard
{
    IReadOnlyList<BountyOffer> Offers { get; }
    IReadOnlyList<BountyContractView> Active { get; }
}
```

- `LocalId` uses 1-64 letters, digits, `_`, `-` or `.`; registration returns `<mod id>:bounty/<local id>`.
- `FactionId` and `TargetRecord` are FCS string IDs. `Required` defaults to 1 and `Reward` to 0.
- Event `Target` is the display name; `Kind` is `faction`, `animal`, `spawned` or `character`; `Objective` is `kill` or `capture`.
- `Capture` is accepted only for `Spawned` contracts; built-in downed named targets already use the capture path.
- `Board` is a snapshot of the current published board. The lists are empty before the first board publish.
- Handler registration order is invocation order. A subscription is removed with `Dispose`.

## Lua mapping

| Lua | C# event | Payload |
|---|---|---|
| `world.start` | `WorldStarted` | `tick`, `seed` (hex), `money`, `game_hours` |
| `world.tick` | `WorldTick` | `tick`, `seed` (hex), `money`, `game_hours` |
| `bounty.accepted` | `BountyAccepted` | `id`, `title`, `target`, `kind`, `objective`, `required`, `reward`, `progress` |
| `bounty.completed` | `BountyCompleted` | same fields; `progress` equals `required` |

`meitou.contracts.register` maps onto `IBountyContracts.Register`; `meitou.events.on` subscribes to the shared bus. A Lua
handler that throws disables only that script and removes its contract definitions. Contracts restored from a save at the first
tick are recorded without replaying `bounty.accepted`; later accepts and completions fire normally.

## Runtime and lifecycle

- `GameHost` creates one `ModApiRuntime` before loading the C# and Lua hosts. It owns one `ModEventBus` and one
  `BountyContractCatalog`.
- `ModPluginHost` gives each plugin a scoped `IBountyContracts`; `LuaModHost` gives each script the same scoped view. A failed
  initialization removes that mod's definitions.
- `ModRuntimeSystem` is appended after the standard systems and publishes `WorldStarted` once, then board changes and
  `WorldTick` on every simulation tick. It records the board's first state without synthetic accepted events.
- The game's `BountySettings.Contracts` is the same catalog, so plugin, script and built-in offers share one board.
- There is no hot reload or unload. Event subscriptions live for the game session.

## Permissions

Script manifests may declare an optional permission list:

```json
"permissions": ["events:world", "events:bounty", "contracts:write"]
```

| Permission | Allows |
|---|---|
| `events:world` | `world.start`, `world.tick` |
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
- Existing accepted/completed contract saves keep their CAMERA state; the first tick suppresses accepted replays.
- The samples are [frontier.bounties](../sdk/samples/frontier.bounties) and
  [frontier.bounty.dust_bandit](../sdk/samples/frontier.bounty.dust_bandit).

## Not implemented

- Commands, scheduler, dialogue, factions, economy, save-state and asset APIs.
- UI/rendering hooks, hot reload, Workshop publishing and remote repositories.
- Removing the trusted C# escape hatch (an API 2 decision).
