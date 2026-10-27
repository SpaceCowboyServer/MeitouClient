# Lua mods

**Status: implemented (2026-10-27).** `src/Meitou.Modding.Scripting` uses MoonSharp 2.0.0 to run `trust: script` manifests in one hard-sandboxed
interpreter per mod. Scripts register contracts and receive the shared `IModEvents` stream described in [modding-api](modding-api.md);
broken script isolation, permissions and cross-language registration are tested. YAML content application, commands, assets, UI and script
save state are not implemented yet.

## Loading

- A script manifest needs `"trust": "script"` and a relative `entry`, for example `scripts/main.lua`; assemblies are refused. See
  [modding-manifest](modding-manifest.md).
- `LuaModHost.Load(ModResolution, GameDatabase, IModApiHost)` reads only enabled scripts, after the data merge and before `StandardSystems.Build`
  ([modding-csharp](modding-csharp.md) has the matching plugin lifecycle). It skips the no-population and flat sandbox modes.
- Entry paths use the same case-folding as game data (`GameFiles.Resolve`), stay inside the mod folder and are limited to 1 MiB.
- A syntax error, a failing initialization, an invalid contract registration, a denied permission or a throwing event handler disables that
  script. The other scripts and the game keep running; its registered contract definitions are removed.
- The game creates one `IModApiHost` (`ModApiRuntime`) and passes it to both the C# and Lua hosts. Lua registers into the same catalog as
  C# and built-in content. There is no Lua board-polling system: `ModRuntimeSystem` publishes the shared events after the standard systems.

## Sandbox

- `CoreModules.Preset_HardSandbox`: `table`, `string`, `math`, `bit32`, iterators, `assert`, `error`, `print` and constants.
- No `io`, `os`, `load`, `dofile`, `require`, `debug`, coroutine or raw CLR types. Lua sees only the `meitou` table and its functions.
- `print` goes to the game log with the mod ID, like `meitou.log.info`.
- There is no instruction/CPU quota or reload yet. A script can still spin inside one callback; the isolation limits are the sandbox and the
  per-script error boundary, not a watchdog.

## API

```lua
meitou.mod_id          -- string
meitou.version         -- manifest version
meitou.api_version     -- number
meitou.random_salt     -- 16-digit hex string, stable per mod

meitou.log.info(text)
meitou.log.warn(text)
meitou.log.error(text)
```

### Permissions

`meitou.json` may declare `permissions`:

```json
"permissions": ["events:world", "events:bounty", "contracts:write"]
```

- `events:world` allows `world.start`/`world.tick`; `events:bounty` allows `bounty.accepted`/`bounty.completed`; `contracts:write`
  allows `meitou.contracts.register`.
- An absent or empty list keeps the API 1 legacy default (all three allowed). A non-empty declared list is enforced strictly; a denied call
  disables the script during initialization.
- Unknown or duplicate permissions are manifest errors. C# plugins are trusted and not gated by this list.

### Contracts

```lua
local id = meitou.contracts.register {
  id = "dust-bandit",             -- local ID: letters, digits, '_' '-' '.'
  kind = "faction",               -- "faction", "animal" or "spawned"
  faction = "Dust Bandits",       -- FACTION string ID or unique name; faction/spawned
  record = "Dust Bandit",         -- ANIMAL_CHARACTER or CHARACTER ID/unique name; animal/spawned
  title = "Dust Bandit Cull",
  detail = "Kill two Dust Bandits", -- optional
  required = 2,                   -- default 1
  reward = 6000,                  -- default 0
  objective = "kill",             -- "kill" or "capture"; default kill
}
```

- The returned/global offer ID is `<mod id>:bounty/<local id>`; it is also the saved contract ID.
- `faction` must be an existing `FACTION`; `animal` needs a non-unique `ANIMAL_CHARACTER`; `spawned` needs a non-unique `CHARACTER` and a `FACTION`.
  Each accepts its string ID or its unique display name.
- `capture` is accepted only for spawned contracts. A built-in downed named target can already use the capture path.
- A spawned contract creates the target squad on accept through the population's sandbox spawner; the target is marked and saved like a named
  target. The squad is idle in this first version.
- Definitions are live: a script may register during initialization or an event, and the board includes it on its next offer refresh. Definitions
  are not saved; accepted/completed contracts are saved in the normal CAMERA `meitou bounty*` keys.
- A definition whose faction/record is missing at board build time does not become an offer. Invalid IDs and duplicate IDs disable the script.

### Events

```lua
meitou.events.on("bounty.accepted", function(b)
  meitou.log.info("accepted " .. b.id)
end)

meitou.events.on("bounty.completed", function(b)
  meitou.log.info("paid " .. tostring(b.reward))
end)
```

| Event | When | Payload |
|---|---|---|
| `world.start` | first tick of each world | `tick`, `seed` (hex string), `money`, `game_hours` |
| `world.tick` | every tick, after board events | same world payload |
| `bounty.accepted` | a contract appears accepted after the script host started | `id`, `title`, `target`, `kind`, `objective`, `required`, `reward`, `progress` |
| `bounty.completed` | a contract changes to completed | same contract payload |

Contracts restored from a save at the first tick are recorded without replaying `bounty.accepted`; subsequent accepts and completions fire normally.
A contract abandoned and later accepted again is treated as a new accepted contract. Handlers run in registration order on the simulation thread.

The sample [`frontier.bounty.dust_bandit`](../sdk/samples/frontier.bounty.dust_bandit) depends on the sample framework and registers a Dust Bandits
contract; it uses only base-game data, so it needs no custom assets.

## Not implemented

- Content-defined contract YAML, localization and consequence data.
- Lua commands, scheduler, asset/content queries, UI hooks and direct simulation access.
- Lua save state; contract state is saved by the board. A small `ISaveable` bridge is planned with lifecycle work.
- Instruction/time budgets, hot reload and unload.
- More domain contracts; the shared C#/Lua event bus is [modding-api](modding-api.md).
