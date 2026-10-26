# Modding foundation

**Status: manifest/resolver, VFS/content validation and startup validation implemented (2026-10-26); applying content to gameplay,
C#/Lua loading and save integration are planned.** The RE_Kenshi decision ([rekenshi](formats/rekenshi.md)) stands: its native DLLs
stay ignored. Data-only Kenshi mods load through the normal readers; Meitou's own mods use this plan.

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

## Principles

- Data first; code only when data cannot express the behavior.
- One asset pipeline and one content registry; IDs, not raw paths.
- Prefer capabilities over engine internals; direct simulation access is a documented exception while the facade grows.
- Target 80% data mods, 15% scripts, 5% plugins. Add a core system only when the game or several mods need it;
  otherwise use a plugin.

## Manifest, versions, trust

`meitou.json` is JSON (source-generated; the launcher is Native AOT). Content uses `content/*.yaml` (game-side).
Legacy Kenshi `.mod` files keep the existing `mods.cfg`/`__mods.list` path; a Meitou manifest is required for
Meitou-specific code, content schemas and assets. The implemented fields, semver/range syntax, resolution rules and
`profile.lock.json` are in [modding-manifest](modding-manifest.md).

Trust tiers are `data`, `script` and `plugin`; `native` is unsupported. IDs are namespaced (`frontier.revolver`), content/assets
use `modid:category/name`, and the launcher refuses Play on missing/disabled dependencies, conflicts or version mismatches.

## Data and assets

The implemented VFS, asset IDs/kinds, YAML content registry and validation are in [modding-content](modding-content.md).
The engine owns loading; mod code gets handles/services. Shaders, native formats and world data are later.

- `content/` YAML: items, factions, contracts, dialogue, localization; schema + validator; stable IDs.
- `assets/`: textures, meshes, audio, fonts, UI sprites; referenced by ID, never raw paths.
- VFS mounts mod roots in load order; later overrides earlier; conflicts are reported.
- Asset pipeline: importers per type, typed handles, sync load/cache/ref-count today; async/streaming later.

## API surface (v1 target)

The API is a facade over the game's systems, used by data, C# and Lua:

- **Core services:** `IWorldQuery`, `IEvents`, `ICommands`, `IScheduler`, `ISave`, `IAssets`, `IContent`, `IUi`
  (core UI/notifications, not mod-drawn panels in v1), `IAudio`, `ILocalization`, `IServices`, `ILog`.
- **Domain services:** contracts/quests, dialogue, factions/reputation, economy, combat, AI, inventory/building/law as
  those systems exist.
- C# mods implement `IMod` and receive `IModContext`; Lua receives the same services. `Meitou.Modding.Api` owns the
  contracts/DTOs/IDs; `Meitou.Game` owns the adapters. A plugin may expose its own service to content packs, with a
  declared dependency/API version.

## Code

### C# (first)

The detailed C# contract is preserved in [modding-csharp](modding-csharp.md): `meitou.json` discovery, one
non-collectible `AssemblyLoadContext` per mod, `IMod.Initialize(ModContext)` after data merge, simulation-only
extension points, the determinism rules, sidecar saves, versioning and trust. This plan adds dependencies, data/assets,
Lua and the launcher around that contract; it does not replace the C# loading/lifecycle decisions yet.

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
| 2 | VFS, asset pipeline, content registry/validation | revolver content/assets validate data-only; missing assets fail validation |
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
- Whether content items map onto FCS records or a new runtime item model.
- Workshop needs Meitou's own Steam app; a local/custom repository comes first.
