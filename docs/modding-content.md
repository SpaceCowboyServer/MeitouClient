# Modding content and assets

**Implemented (2026-10-26):** the JIT-only `src/Meitou.Modding` project provides the VFS, stable asset IDs/kinds, YAML content parsing
and validation, and a reference-counted asset cache. The game validates enabled mods at startup and refuses to start on errors;
`meitou-tools mods` does the same validation from the command line. Content definitions are not applied to gameplay yet. The Native
AOT launcher must not reference this project, so it stays on `Meitou.Core` and the manifest only.

## Layout

A manifest declares its mounted roots, relative to the mod folder:

```json
"assets": ["assets/"],
"content": ["content/"]
```

```text
mods/FrontierRevolver/
  FrontierRevolver.mod
  meitou.json
  assets/weapons/revolver.png
  assets/weapons/revolver.mesh
  assets/weapons/revolver_shot.wav
  content/items/revolver.yaml
```

Asset IDs are `modid:relative/path` below an asset root, so the file above is
`frontier.revolver:weapons/revolver.png`. IDs are mod-scoped; a relative-path lookup (`ModVfs.Find("weapons/revolver.png")`) lets a
later mod override an earlier one, as the original art override rules do. The same relative path in two roots of one mod is reported
as a duplicate; the later mount wins.

## Content YAML

One definition per YAML file. `id` is local to the mod; the registry prefixes the mod and type, so this file becomes
`frontier.revolver:item/revolver`:

```yaml
id: revolver
type: item
name: Frontier Revolver
description: A heavy pre-war revolver.
assets:
  icon: frontier.revolver:weapons/revolver.png
  mesh: frontier.revolver:weapons/revolver.mesh
  sound: frontier.revolver:weapons/revolver_shot.wav
data:
  value: 120
```

- `id` and `type` are required; `type` is lowercase (`[a-z][a-z0-9_-]*`), `id` allows letters, digits, `_`, `-` and `.`.
- `schema` defaults to 1; this build accepts 1.
- `name` and `description` are optional for generic types. `item` needs a name.
- `assets` maps roles to full asset IDs, never disk paths. Unknown roles are allowed for custom schemas.
- `data` is the type-specific payload; typed schemas read it later.

### Built-in schemas

| Type | Validation |
|---|---|
| `item` | non-empty `name`; required `icon` texture and `mesh` mesh; optional `sound` audio |
| `faction`, `contract` | non-empty `name` |
| `dialogue`, `localization` | common fields only |

Unknown types are errors. A plugin can create a `ContentSchemaRegistry` with additional `IContentSchema` implementations; the
loader uses the registry it is given. `ContentRegistry.FindItem(id)` returns the typed `ItemContent` (name, description, icon/mesh/sound
asset IDs and `data`) when the required roles are valid.

## Validation

`ModContentBundle.Load(resolution)` mounts the resolved mods, reads every `*.yaml`/`*.yml` under the content roots, and returns the
valid definitions plus every problem. `meitou-tools mods` does the same for the install's `mods.cfg`:

```bash
dotnet run --project tools/Meitou.Tools -- mods
```

Errors include missing asset roots, missing referenced assets, an asset whose extension does not match its role, duplicate content
IDs, malformed YAML, an unknown content type, a newer schema, and unsafe paths. Warnings include a later mod overriding the same
asset path and duplicate mounts. Every content file is limited to 1 MiB.

## Asset pipeline

`AssetPipeline` resolves an `AssetId` through the VFS, picks the first `IAssetImporter` that accepts the file, and caches the imported
value:

- `AssetHandle<T>` is ref-counted; the last `Dispose` disposes a cached `IDisposable` and drops it.
- Acquiring the same ID twice shares one value; a wrong requested type is an error.
- No importer is registered yet for real textures, meshes or audio. The game will register those importers when the content is wired
  into rendering, audio and simulation.
- Importers run synchronously on the caller's thread and must not keep the stream.

## Not implemented

- Content definitions are parsed and validated, but do not create or modify `GameDatabase` records or appear in gameplay yet. Lua contract
  definitions use the separate `meitou.contracts.register` API ([modding-lua](modding-lua.md)), not YAML `contract` content.
- Texture, mesh and audio importers are not registered by the game; only the cache/refcount contract exists.
- Asynchronous asset loading, streaming, and GPU upload are later.
- Content-to-content references and dependency validation are not implemented.
- Manifest-only mod folders without a `<name>.mod` are not discovered yet.
- The launcher does not write `profile.lock.json` or validate YAML (it is Native AOT).
