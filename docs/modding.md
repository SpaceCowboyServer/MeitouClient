# Modding foundation

**Status: plan, not implemented (2026-10-26).** The RE_Kenshi decision ([rekenshi](formats/rekenshi.md)) stands:
its native DLLs stay ignored. Data-only Kenshi mods load through the normal readers; Meitou's own mods use this plan.

Goal: a data-first modding foundation with a stable API, compiled C# extensions first, Lua later, and a launcher
that resolves dependencies, load order and trust. Non-goals for v1: RE_Kenshi binary compatibility, native plugins,
Steam Workshop, a visual editor, a custom general-purpose language.

## First slice

Three mods prove the foundation:

| Mod | Type | Proves |
|---|---|---|
| `frontier.revolver` | data + assets | item, icon, mesh, sound, load order |
| `frontier.bounties` | C# plugin | contract service, events, save, core bounty UI |
| `frontier.bounty.dust_bandit` | data + Lua | contract content, depends on both, consequence |

Done when: the launcher resolves the three; the revolver item appears; a bounty is offered, tracked, paid and saved;
disabling the revolver or the framework fails with a clear message; `--validate-mods` reports missing deps/assets;
a broken Lua script does not crash the game.

## Layers

| Layer | Responsibility |
|---|---|
| Manifest, discovery, resolver | `meitou.json`, dependencies, semver, load order, profile lockfile, trust |
| VFS and assets | mount mod folders, override by load order, typed asset IDs/handles |
| Content | YAML definitions, schemas, validation, registry |
| API facade | stable contracts and lifecycle; direct simulation access is an open decision |
| C# plugins | `AssemblyLoadContext`, API version, trusted opt-in |
| Lua | sandboxed scripts over the same API |
| Lifecycle/save | mod list/versions in save, sidecar state, safe mode |
| Launcher/tooling | mod list, deps, conflicts, profiles, `meitou-tools mod` |

## Manifest, versions, trust

`meitou.json` is JSON (source-generated; the launcher is Native AOT). Content uses `content/*.yaml` (game-side).
Legacy Kenshi `.mod` files keep the existing `mods.cfg`/`__mods.list` path; a Meitou manifest is required for
Meitou-specific code, content schemas and assets.

```json
{
  "id": "frontier.bounty.dust_bandit",
  "version": "1.0.0",
  "apiVersion": 1,
  "gameVersion": ">=0.16 <0.17",
  "depends": { "frontier.bounties": ">=1.2 <2", "frontier.revolver": ">=1.0 <2" },
  "optional": { "frontier.weather": ">=0.5" },
  "provides": ["frontier.contracts@1"],
  "loadAfter": ["frontier.bounties"],
  "content": ["content/"],
  "assets": ["assets/"],
  "trust": "script",
  "entry": "scripts/main.lua"
}
```

- C# mods add `"assemblies": ["lib/Foo.dll"]` and an entry type (`"entry": "Foo.FooMod"`).
- IDs are namespaced (`modid`); content/assets are `modid:category/name`.
- Hard deps must resolve; optional deps enable extra behavior; cycles fail.
- A profile writes `profile.lock.json` with resolved versions, checksums, order and trust. Saves record it.
- Trust: `data` (validated), `script` (Lua sandbox), `plugin` (C# trusted/opt-in). `native` is unsupported.
- Versions: mod semver, API major, game range, content schema version.

## Data and assets

- `content/` YAML: items, factions, contracts, dialogue, localization; schema + validator; stable IDs.
- `assets/`: textures, meshes, audio, fonts, UI sprites; referenced by ID, never raw paths.
- VFS mounts mod roots in load order; later overrides earlier; conflicts are reported.
- Asset pipeline: importers per type, typed handles, async load/cache, ref-count, validation.
- The engine owns loading; mod code gets handles/services. Shaders, native formats and world data are later.

## Code

### C# (first)

The current concept is the first implementation; the rest of this plan does not replace it:

- Discovery: a folder with `meitou.json` (`id`, `apiVersion`, `assemblies`, `entry`). `ModInfo` reports code mods next
  to `HasNativePlugin`; the launcher shows them. Load set = enabled `mods.cfg` entries with a manifest; a disabled
  mod's DLL is never opened.
- Loading: one non-collectible `AssemblyLoadContext` per mod, loaded once. `Meitou.*` and shared third-party assemblies
  resolve from the default context; other dependencies come from the mod folder. No unload/hot reload. The game stays
  JIT (no trim/AOT); the launcher AOT is unaffected. Mods compile against the release's `Meitou.*.dll` and
  `Meitou.Modding.dll`.
- Lifecycle: `IMod.Initialize(ModContext)`; the type comes from `entry` (no reflection scan). Runs after data merge,
  before `StandardSystems.Build` returns, in `mods.cfg` order. `ModContext` exposes the merged `GameData`, mod folder,
  logger, mod-scoped `Rng` salt, `Enqueue(SimCommand)` and registrations. A throwing `Initialize` disables that mod.
- Extension points: tick systems at `StandardParts` anchors (`IStateHashed` feeds `World.StateHash`); dialogue
  condition/effect registries for codes above vanilla; console/`SimCommand` commands; custom FCS record types
  (merge/save survival is unknown until tested). No UI/rendering hooks in the first version; core UI may read mod
  data/services.
- Determinism: mod systems follow [simulation](simulation.md) threading; parallel phases write only their partition's
  `Next`, read `Previous`/grid, use `EffectBuffer`; randomness via `Rng.Hash` with the mod salt; no `World` access
  outside the tick; input through `Enqueue`. A double-run `StateHash` debug mode would catch violations.
- Persistence: vanilla saves stay byte-exact; mod state goes in a sidecar per mod keyed by manifest id
  (`ISaveable.Write/Read`). A save without the mod keeps the sidecar; the original game ignores it.
- Versioning/trust: the contract assembly covers lifecycle, registration and persistence; mods reference
  `Meitou.Simulation` directly for `World`/`CharacterTable`/`EffectBuffer` (facade-only is open). `apiVersion` mismatch
  refuses the mod. A workshop DLL is arbitrary code: the launcher marks it, the game confirms each new one on first
  run; no sandbox.

### Lua (later)

- MoonSharp or NLua; same API facade; sandboxed, explicit permissions, no raw file/network.
- Events, commands, content registration, scheduler; errors disable the script, not the game.
- Hot reload is a development feature; no unload for C#.

## Projects

| Project | Responsibility |
|---|---|
| `src/Meitou.Core/Modding` | manifest, resolver, profiles, lockfile (AOT, launcher + game) |
| `src/Meitou.Modding.Api` | stable contracts/DTOs/IDs; no engine internals |
| `src/Meitou.Modding` | VFS, content, assets, services, lifecycle |
| `src/Meitou.Modding.Scripting` | Lua host and bindings |
| `src/Meitou.Modding.Plugins` | C# ALC host and version gate |
| `src/Meitou.Game` | adapters and composition root |
| `launcher/Meitou.Launcher` | mod list, deps, conflicts, profiles, trust |
| `tools/Meitou.Tools` | `mod init/validate/pack/graph` |
| `docs/modding*` | manifest, content, API, trust, saves |

Dependency direction: `Api` has no engine dependencies; `Modding` references `Core`/`Data`; `Game` wires adapters;
the launcher uses `Core` only and loads no mod code.

## Phases

| Phase | Work | Exit |
|---|---|---|
| 0 | Spec, sample manifests/content, ID/version/trust rules | docs + samples |
| 1 | Manifest discovery, resolver, profiles/lockfile, launcher display | three mods resolve; cycles/missing deps reported |
| 2 | VFS, asset pipeline, content registry/validation | revolver loads data-only; missing assets fail validation |
| 3 | API facade + C# host/adapters | bounty framework loads, registers, saves |
| 4 | Bounty vertical slice | accept/track/complete/save; dependency failures safe |
| 5 | Lua host/sandbox | Lua bounty content; broken script isolated |
| 6 | Save/lifecycle/diagnostics | remove mod, safe mode, `--validate-mods` |
| 7 | SDK/docs/distribution | mod template; local repo; Workshop later |

Order: data → C# → Lua. Do not build the distributor or Lua host before the data/API layers.

## Acceptance

- Manifest parser, semver, resolver, cycle detection, lockfile.
- VFS overrides/load order; content schema and missing asset references.
- C# discovery/load, API mismatch, error isolation.
- Lua sandbox, permissions and broken-script isolation.
- Save/load with missing or disabled mods; safe mode.
- Launcher dependency/conflict/trust display.
- 100-mod load-order test; asset/event performance.
- Path traversal, malformed YAML, oversized assets and malicious scripts.

## Open decisions

- Manifest JSON is chosen for the AOT launcher; content YAML is game-side. Revisit only if one format is clearly better.
- Non-collectible ALC/no unload is the current C# choice; collectible contexts remain open.
- Lua host (MoonSharp vs NLua) and whether scripts get UI hooks.
- Whether mods reference `Meitou.Simulation` directly or only the facade.
- Workshop needs Meitou's own Steam app; a local/custom repository comes first.
