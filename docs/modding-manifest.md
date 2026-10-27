# Meitou mod manifest

**Implemented** in `src/Meitou.Core/Modding` (2026-10-26). The launcher validates enabled manifests before Play; the game loads C# plugins and
Lua scripts from enabled manifests. YAML content is validated but not applied to gameplay yet. A missing manifest keeps the original Kenshi
data-mod rules.

## File

`meitou.json` sits beside a mod's `<name>.mod`. It is JSON (the launcher is Native AOT, so it uses the source-generated
`ModdingJson` context), camelCase, with trailing commas accepted and unknown fields ignored.

```json
{
  "id": "frontier.bounty.dust_bandit",
  "version": "1.0.0",
  "apiVersion": 1,
  "gameVersion": ">=0.16 <0.17",
  "depends": { "frontier.contracts": ">=1.0 <2", "frontier.bounties": ">=1.2 <2", "frontier.revolver": ">=1.0 <2" },
  "optional": { "frontier.weather": ">=0.5" },
  "loadAfter": ["frontier.bounties"],
  "permissions": ["events:bounty", "contracts:write"],
  "content": ["content/"],
  "assets": ["assets/"],
  "trust": "script",
  "entry": "scripts/main.lua"
}
```

| Field | Meaning |
|---|---|
| `id` | Stable namespaced ID, at least two dot-separated segments (`frontier.revolver`). Case-sensitive. |
| `version` | The mod's semver, normally `major.minor.patch` with optional prerelease/build. |
| `apiVersion` | `Meitou.Modding.ModApi.CurrentVersion`; a different value refuses the mod. |
| `gameVersion` | Optional range checked against the running game version. |
| `depends` | Mod ID -> version range. Hard dependencies; missing, disabled or wrong-version targets are errors. |
| `optional` | Same shape; applied only when the target is enabled. A disabled target is a warning. |
| `provides` | Capabilities such as `frontier.contracts@1`. A dependency on the capability ID checks the capability version, or the provider's mod version when `@` is absent. |
| `loadAfter` | Soft ordering edges to enabled mods or capability providers. Missing targets are ignored. |
| `conflicts` | Enabled mods or capability providers that cannot be loaded together. |
| `permissions` | Script capabilities: `events:world` (world/day events), `events:bounty`, `contracts:write`. Unknown/duplicate entries are errors; an absent or empty list keeps the API 1 legacy defaults. C# plugins are trusted. |
| `content` | Relative content roots, e.g. `content/`; must stay inside the mod folder. Parsed in Phase 2. |
| `assets` | Relative asset roots; same path rules. Parsed in Phase 2. |
| `trust` | `data`, `script` or `plugin` (lowercase). Native plugins are unsupported. |
| `entry` | C# entry type for `plugin`; relative script path for `script`; ignored for `data`. |
| `assemblies` | C# `plugin` only: relative `.dll` paths inside the mod folder. |

A `plugin` manifest must have `assemblies` and `entry`; a `script` manifest must have an entry path and no assemblies. `data` mods
cannot ship assemblies. Duplicate `provides` entries are refused. A framework provider advertises its capability, for example
`"provides": ["frontier.contracts@1"]`; content packs depend on `frontier.contracts` without providing it.

## Versions

- Mod and capability versions use semver 2.0.0. Build metadata is ignored for precedence.
- A range is whitespace/comma-separated comparators ANDed together: `>=1.2 <2`, `1.2.3`, `*`.
- Missing numeric parts in a comparator are zero: `<2` means `<2.0.0`, `>=1.2` means `>=1.2.0`.
- `||`, caret and tilde ranges are not supported. Use explicit comparators.
- Dependency ranges must not be empty; write `*` for any version.

## Resolution

- Enabled mods come from `mods.cfg` folder names mapped to manifest IDs; a disabled mod's manifest does not satisfy a dependency.
- Hard dependencies, API/game versions, duplicate IDs/capabilities and conflicts are errors. Optional dependencies and `loadAfter`
  are soft.
- The user's order is the tie-break, so the same enabled list resolves to the same load order. Hard dependency and `loadAfter` edges
  are then applied; cycles are errors.
- If there is an error, the result still lists every enabled mod (for display) but `ModResolution.Success` is false and the launcher
  refuses to play.

## Lockfile

`profile.lock.json` is written from a successful resolution:

```json
{
  "format": 1,
  "apiVersion": 1,
  "mods": [
    { "id": "frontier.revolver", "version": "1.0.0", "folder": "Revolver", "trust": "data", "manifestSha256": "..." }
  ]
}
```

The mods are in load order. The hash currently covers only `meitou.json`; content/asset hashes wait for Phase 2. Reading a newer
`format` returns null.

## Not implemented

- Content YAML and asset roots are mounted and validated by `Meitou.Modding` ([modding-content](modding-content.md)), but their
  definitions do not affect gameplay yet.
- C# `plugin` assemblies load through [modding-csharp](modding-csharp.md); Lua `script` entries load through [modding-lua](modding-lua.md).
- Manifest-only mod folders (no `<name>.mod`) are not discovered yet.
- The launcher does not write a lockfile or offer automatic dependency enabling yet.
- Saves record the enabled IDs/versions/trust in CAMERA `meitou mod*` keys and code state in `meitou-mods/<id>.bin` sidecars, not in the vanilla save or a lockfile.
