# Generic mod API

**Status: proposed (2026-10-27), not implemented.** The bounty samples prove one shared contract catalog and a working
Faction-tab board. They do not yet expose a stable cross-language API: C# gets `GameDatabase`/`World`, Lua gets a small
whitelisted API, and only Lua receives bounty events.

This plan turns the bounty prototype into a reusable contract. One semantic API is exposed through two adapters:

```text
C# plugin ──┐
            ├── IModEvents / IBountyContracts / IBountyBoard ── game adapters ── simulation
Lua script ─┘
```

## Rules

- Public signatures use API DTOs and primitive values, never `World`, `GameDatabase`, `FcsRecord` or a simulation system.
- C# and Lua share event names, payload fields and registration rules; they do not share CLR types.
- Stable APIs are opt-in. The existing `Data`/`AddSystem`/`Enqueue` surface remains a trusted-plugin escape hatch for API 1,
  but samples and docs stop using it as the normal path.
- Game-side adapters own the translation. `Meitou.Simulation` does not reference `Meitou.Modding.Api`.
- Events are read-only notifications on the simulation thread; mutations go through queued commands.
- One throwing handler disables or reports only that handler; it never stops the board, the world or other mods.
- Every domain gets its own capability version, so adding factions/dialogue later does not force every mod to bump.

## Public contracts

Start with the contracts the two samples actually need.

```csharp
public interface IModEvents
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);
}

public sealed record WorldStarted(ulong Seed, long Tick);
public sealed record WorldTick(long Tick, int Money, double GameHours);

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
    string Detail,
    string TargetRecord,
    string FactionId,
    int Required,
    int Reward);

public sealed record BountyOffer(
    string Id, string Title, string Detail, int Required, int Reward, float Distance);

public sealed record BountyContractView(
    string Id, string Title, int Progress, int Required, int Reward, float Distance);

public interface IBountyContracts
{
    /// <summary>Registers a local ID; the adapter returns the canonical mod-qualified ID.</summary>
    string Register(BountyDefinition definition);
}

public interface IBountyBoard
{
    IReadOnlyList<BountyOffer> Offers { get; }
    IReadOnlyList<BountyContractView> Active { get; }
}
```

`IModContext` gains:

```csharp
IModEvents Events { get; }
IBountyContracts Bounties { get; }
IBountyBoard Board { get; }
```

The framework plugin is then a normal API consumer, not the owner of the game's board.

## Lua mapping

Lua sees the same contract through the existing names, backed by the API adapters:

```lua
meitou.contracts.register { id = "dust-bandit", kind = "faction", ... }

meitou.events.on("bounty.accepted", function(b) ... end)
meitou.events.on("bounty.completed", function(b) ... end)
meitou.events.on("world.start", function(w) ... end)
meitou.events.on("world.tick", function(w) ... end)
```

- Lua local IDs are prefixed by the adapter exactly as now.
- Event tables mirror the C# record fields (`id`, `title`, `target`, `kind`, `objective`, `required`, `reward`, `progress`).
- Unknown event names, unpermitted events and non-function handlers are rejected at registration.
- The Lua host no longer polls board state for bounty events; it subscribes to `IModEvents`.

## Runtime

Add a JIT-only `src/Meitou.Modding.Runtime` project referencing `Meitou.Modding.Api` and `Meitou.Simulation`.

- `ModEventBus` implements `IModEvents`; handlers run in registration order, isolated, on the simulation thread.
- `ModRuntimeSystem : ITickSystem` is appended after the standard systems. It publishes `WorldStarted` once per world and
  `WorldTick` each tick.
- `BountyEventAdapter` maps board offers/active contracts to API DTOs and diffs board state into `BountyAccepted` /
  `BountyCompleted`. It is the only place that knows both the simulation catalog and the API DTOs.
- `GameHost` creates one bus and one runtime system after the standard systems, then gives the bus to the plugin host and the
  Lua bridge. `Meitou.Modding.Plugins` and `Meitou.Modding.Scripting` depend only on `Meitou.Modding.Api` for these types.

The first implementation should keep the current `BountyContractCatalog` internally and adapt it; replacing the simulation
board is not part of this step.

## Manifest and permissions

Add an optional `permissions` array:

```json
"permissions": ["events:world", "events:bounty", "contracts:write"]
```

- Known permissions are validated; unknown ones are errors.
- C# plugins are trusted, but still use the same event names and payloads.
- Lua checks permissions before subscription/registration.
- For compatibility, existing bounty/world events and `contracts:write` remain allowed when `permissions` is absent in API 1.
  New domains require explicit permissions. API 2 can make all permissions explicit.

Keep `apiVersion: 1` while the change is additive. Add capability versions for each domain:

```text
frontier.contracts@1
frontier.bounty.events@1
frontier.world.events@1
```

## Implementation order

### 8A — Contracts and bus

1. Add API DTOs, `IModEvents`, `IBountyContracts`, `IBountyBoard`.
2. Add `Meitou.Modding.Runtime` with `ModEventBus`.
3. Add `ModRuntimeSystem` for world start/tick.
4. Wire `IModContext.Events` and the plugin host.
5. Tests: subscribe, ordering, unsubscribe, throwing handler isolation.

### 8B — Bounty adapter

1. Add `BountyEventAdapter` and `GameHost` wiring.
2. Expose `IModContext.Bounties`/`Board`.
3. Migrate `sdk/samples/frontier.bounties` to use the stable contracts and a `BountyCompleted` subscription.
4. Tests: C# registers a contract, it appears on the board, and the C# handler receives accepted/completed.

### 8C — Lua bridge

1. Give `LuaModHost` the shared `IModEvents` and `IBountyContracts`.
2. Back `meitou.events.on` and `meitou.contracts.register` with the API.
3. Remove Lua board polling from `LuaWorldSystem`.
4. Tests: Lua and C# register into one board and both receive the same accepted/completed events.

### 8D — Permissions and compatibility

1. Add `permissions` parsing/validation and Lua checks.
2. Document legacy defaults.
3. Add API compatibility tests for the samples.

### 8E — Next domains

Repeat the pattern one domain at a time: dialogue, factions/relations, commands/queries, save state, then assets/UI.
Do not add a god interface or expose arbitrary systems.

## Migration rules

- Existing C# `Data`, `AddSystem`, `AddSaveable` and `Enqueue` remain for API 1 but are not part of the stable contract.
- Existing Lua scripts keep working through the compatibility adapter.
- Current accepted/completed contract save state remains in CAMERA; no migration format is needed.
- The sample framework plugin no longer owns the catalog once `GameHost` supplies `IBountyContracts`.
- `frontier.revolver` remains blocked on assets and is not part of this API work.

## Acceptance

- The C# framework sample and the Lua Dust Bandit sample register through the same API and produce one set of offers.
- Both receive `bounty.accepted` and `bounty.completed` with equal payload values.
- A throwing C# handler does not affect Lua or the board; a throwing Lua handler disables only that script.
- No sample uses `World`, `GameDatabase` or `BountyBoardSystem` directly.
- `world.start` fires once per world; `world.tick` fires each simulation tick after the board.
- Build clean; quick suite passes; AOT launcher publish still succeeds.
- Documentation covers C#/Lua payloads, permissions, capability versions and the escape-hatch policy.

## Not in the first slice

- UI or rendering hooks, custom panels, assets/audio access.
- Scheduler and arbitrary commands.
- Dialogue, factions, economy or other domain contracts.
- Hot reload/unload, Workshop publishing, remote repositories.
- Removing the trusted C# escape hatch; that is an API 2 decision.
