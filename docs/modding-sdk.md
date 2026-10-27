# Mod SDK and local distribution

**Implemented (2026-10-27):** `tools/Meitou.Sdk` and `meitou-tools mod ...` scaffold, validate, pack and install a mod without an install.
The launcher's **Install mod...** button and drag-and-drop install packages without the CLI. A local repository is a folder of
`.meitoumod` packages plus a generated index; Workshop is later. The three no-asset samples live in [`sdk/samples`](../sdk/samples).

## Quick start

```bash
# A data/content starter
dotnet run --project tools/Meitou.Tools -- mod init Mods/Hello --id author.hello --template data

# A Lua starter
dotnet run --project tools/Meitou.Tools -- mod init Mods/Hello --id author.hello --template script

# A C# starter (source only until its assembly is built)
dotnet run --project tools/Meitou.Tools -- mod init Mods/Hello --id author.hello --template plugin
```

`mod init` writes a manifest, a small empty `<folder>.mod` (the discovery file the original game understands), and one starter:

| Template | Files |
|---|---|
| `data` | `content/example.yaml` |
| `script` | `scripts/main.lua` |
| `plugin` | `src/Plugin.cs`, `Plugin.csproj`; build it before validation/packing |

A plugin project needs the game's API assemblies. Set `MEITOU_API_DIR` to the folder containing `Meitou.Modding.Api.dll`,
`Meitou.Simulation.dll`, `Meitou.Data.dll` and `Meitou.Core.dll` (a release build's folder), or set the MSBuild property
`MeitouApiDir`. The sample plugins use project references because they live in this repository.

## Commands

```bash
meitou-tools mod validate <folder>
meitou-tools mod pack <folder> [--out file.meitoumod] [--force] [--skip-validation]
meitou-tools mod install <package|id> [--mods dir] [--repo dir] [--version v] [--force]
meitou-tools mod repo <folder> [--index file]
meitou-tools mod graph [folder]
```

- `validate` checks the manifest, content YAML, asset references and code-file paths in one source folder. It does not need a game install
  and does not resolve installed dependencies.
- `pack` validates first, then writes one `.meitoumod` zip. A source folder without `<folder>.mod` gets a generated empty `.mod` in the package;
  the source is not modified. A folder with a `.mod` must use `<folder>.mod` so the launcher can discover it.
- `install` extracts a package into `<Kenshi>/mods/<folder>` (or `--mods`). It never edits `mods.cfg`; enable the mod in the launcher.
  Existing folders are refused unless `--force`. The launcher's **Install mod...** button and drag-and-drop use the same AOT-safe package
  installer, refresh the list, enable new mods and save `mods.cfg`; replacing an installed folder asks first.
- `repo` writes `meitou-repository.json` beside the packages. `install <id> --repo <folder> [--version v]` chooses the exact version or the
  newest one. Workshop is deliberately not part of this local format.
- `graph` prints the resolved install graph, or a source manifest's dependencies.

## Package format

A `.meitoumod` is a zip with exactly one top-level folder:

```text
frontier.bounty.dust_bandit/
  frontier.bounty.dust_bandit.mod
  meitou.json
  scripts/main.lua
```

Packaging is deterministic (fixed 1980-01-01 timestamps), skips `bin/`, `obj/`, `.git/`, `.vs/` and `dist/`, and limits extracted content to
512 MiB and 10,000 files. Install validates entry paths, extracts to a temporary folder, verifies the manifest, then moves the folder into
place. The original game ignores the package format and the Meitou manifest; after installation it sees an ordinary `.mod` and its data.

## First-slice samples

- [frontier.contracts](../sdk/samples/frontier.contracts): foundation C# plugin; provides the `frontier.contracts@1` capability and
  `IContractFramework`, owns the reusable bartender offer/turn-in/day-refresh behavior and its save state, and has no Dust Bandit
  content.
- [frontier.bounties](../sdk/samples/frontier.bounties): content C# plugin; depends on `frontier.contracts` and registers three Dust
  Bandit templates through `IContractFramework`.
- [frontier.bounty.dust_bandit](../sdk/samples/frontier.bounty.dust_bandit): data-less Lua content; depends on `frontier.bounties`
  (which pulls in the foundation), registers a global Dust Bandits faction contract, declares `events:world`, `events:bounty` and
  `contracts:write`, and logs accepted/completed events and `day.changed`.

`frontier.revolver` remains blocked on custom icon/mesh/sound assets; a data-only stand-in would not demonstrate the asset pipeline.

## Not implemented

- Workshop publishing/discovery and a signed remote repository.
- Automatic dependency installation, package updates and rollback (install is copy-only).
- Enabling mods from `mod install` (the launcher owns `mods.cfg`).
- Lua/C# syntax or entry-type compilation during `validate`; it checks that code files exist, and the runtime hosts isolate broken code.
- Content definitions affecting gameplay; YAML remains validated but not applied.
