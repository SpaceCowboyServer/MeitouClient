# C# modding API

**Status: implemented (2026-10-27).** Enabled `trust: plugin` mods load after the data merge and register stable events/contracts,
services, systems and saveables. The same contracts are available to Lua through [modding-lua](modding-lua.md); the full DTO/event
reference is [modding-api](modding-api.md). RE_Kenshi DLLs stay ignored ([rekenshi](formats/rekenshi.md)). The game remains a JIT
build; the Native AOT launcher loads no mod code.

## Discovery and loading

- A mod carries code when its folder (next to the `.mod`) has `meitou.json` with `"trust": "plugin"`, at least one `assemblies` path and
  an `entry` type. A mod may also carry `RE_Kenshi.json`; those DLLs are ignored.
- `ModPluginHost` loads the enabled mods from `ModResolution.Order`; disabled mods' DLLs are never opened. A missing assembly, missing
  entry type, API mismatch or throwing `Initialize` logs an error and skips that mod; the others load.
- One non-collectible `AssemblyLoadContext` per mod, loaded once. Resolution tries the default context first, so `Meitou.*` and shared
  framework assemblies keep one type identity; other dependencies come from DLLs under the mod folder. No unload, no hot reload.
- The game must stay JIT (self-contained, never `PublishTrimmed`/`PublishAot`). Mods compile against the release's `Meitou.Modding.Api.dll`,
  `Meitou.Simulation.dll`, `Meitou.Data.dll` and `Meitou.Core.dll`.

## Lifecycle

- `IMod.Initialize(IModContext)` in `Meitou.Modding.Api`; the type comes from the manifest's `entry` (no reflection scan).
- The host runs in `GameHost.BootSimulation`, after the game-data merge and population data are ready, before `StandardSystems.Build`.
  Registration does not run for a no-population or flat sandbox world.
- `IModContext` exposes the merged `GameDatabase`, folder, version, API version, a stable `RandomSalt`, `IModLog` and `IModServices`.
- A throwing `Initialize` disables that mod for the session and logs; it does not stop the game.

## Registration

```csharp
public interface IMod { void Initialize(IModContext context); }

public interface IModContext
{
    string Id { get; }
    string Version { get; }
    string Folder { get; }
    int ApiVersion { get; }
    GameDatabase Data { get; }
    ulong RandomSalt { get; }
    IModLog Log { get; }
    IModServices Services { get; }
    IModEvents Events { get; }
    IBountyContracts Bounties { get; }
    IBountyBoard Board { get; }
    IDialogue Dialogue { get; }
    void AddSystem(Func<ITickSystem> factory);
    void AddSaveable(ISaveable saveable);
    void Enqueue(SimCommand command);
}
```

- **Events:** `context.Events.Subscribe<WorldTick>(...)`, `WorldDayChanged`, and the other DTOs in [modding-api](modding-api.md).
  Handlers run on the simulation thread; a throwing handler is logged without stopping other handlers or the world.
- **Bounties:** `context.Bounties.Register(new BountyDefinition(...))` registers one content contract; `context.Board` reads the
  current offers/active/journal views. A non-empty `Giver` makes a bartender contract; it is hidden from the Faction tab, becomes
  `Ready` when done and pays through `TurnIn`. `BountyTarget.Character` picks one nearby non-unique character (by faction, record, or both) at
  offer time and marks it. `DeadlineHours` expires an unfinished accepted contract; `BountyStatus` exposes
  accepted/ready/completed/failed/expired, and `BountyReady`/`BountyFailed`/`BountyExpired` are published events. Pass the giver's
  stable `DialogueContext.NpcId`/`NpcName` to `IBountyBoard.Accept` so giver death and the journal work. The game owns the catalog, so
  a plugin no longer provides one under `ModServiceKeys.BountyContracts` (that key is legacy only).
- **Dialogue:** `context.Dialogue.AddOption(...)` adds dynamic choices to an NPC conversation. `DialogueContext` identifies the NPC,
  town, day, trader/bartender role and whether the first page is showing; `Selected` runs on the simulation thread.
- **Day changes:** `WorldDayChanged` fires once when the game clock crosses midnight; use it to refresh giver batches.

- **Systems:** `AddSystem(Func<ITickSystem>)` appends a factory after the standard systems. The factory runs once for each new world, in
  mod load order and registration order; the first version has no before/after anchors. Plugin systems obey the determinism contract in
  [simulation](simulation.md) "Threading": parallel phases write only their partition's `Next`, use `EffectBuffer` for cross-character
  effects, and take randomness from `Rng.Hash` with `RandomSalt` mixed into the key.
- **Commands:** `Enqueue` goes straight to `World.Commands` once a world exists; before the first world it is held and flushed when the
  world is attached. Commands still carry the usual required `Tick`.
- **Services:** a string-keyed registry shared by the loaded mods (`Provide`, `TryGet`, `Get<T>`). Earlier mods register first; a duplicate
  key is an error. Typed services work when both mods reference the same contract assembly loaded in the default context. Cross-mod
  private contract assemblies are not unified yet.
- **Contract framework:** a content pack can depend on `frontier.contracts` and call
  `context.Services.Get<IContractFramework>(ModServiceKeys.Contracts)` to register `ContractTemplate`s. `Weight` biases the
  deterministic daily slate; `CooldownDays` holds a turned-in template out of that giver's slate. The framework interface and DTOs live in
  `Meitou.Modding.Api`, so the service keeps type identity across plugin load contexts.
- **Shared bounty registry:** `GameHost` creates one catalog and gives the same scoped API to C# and Lua, so both tiers share one offer
  set. The [frontier.contracts foundation](../sdk/samples/frontier.contracts) implements the reusable bartender framework
  (`IContractFramework` and `frontier.contracts@1`); the [frontier.bounties content pack](../sdk/samples/frontier.bounties) depends on
  it and registers three Dust Bandit templates.
- **Logging:** `Info`/`Warn`/`Error` write to the game's console/log with the mod ID.

## Saves

- `ISaveable` is one stable-keyed stream per mod state:
  ```csharp
  public interface ISaveable
  {
      string Key { get; }
      void Write(Stream stream);
      void Read(Stream stream);
  }
  ```
- All saveables of a mod are written into `<save>/meitou-mods/<mod id>.bin`. Capture happens on the tick thread during a save; the file is
  written by the save worker. A load reads and applies the sidecars before the next world's systems are built. The files sit outside the
  known save files; whether the original game ignores them is **Unknown** until tested.
- Missing sidecars leave the mod's state at its defaults; a missing key or malformed file logs and leaves the rest of the load alone.
- The sidecar has a magic/format header and 16 MiB per-mod limit. It is not part of `quick.save`; the vanilla save remains byte-exact.

## Not implemented

- Before/after `StandardParts` anchors; every mod system is appended.
- Dialogue condition/effect registries, console/`SimCommand` command registration, and custom FCS record types.
- Lua dynamic dialogue options; Lua has the day/bounty events and the `giver` contract field.
- UI or rendering hooks.
- Shared save sidecars and command/resource access from Lua.
- Collectible contexts, unload and hot reload.
- Merging separately loaded private contract assemblies for typed cross-mod services.
- Removing the trusted `World`/`GameDatabase` escape hatch; it is an API 2 decision.
- Automatic dependency installation and save migration when a mod is removed or upgraded.
