# Meitou mod SDK

Starter and distribution tools live in `tools/Meitou.Sdk`; the command is `meitou-tools mod ...` from the repository root.

- [SDK guide](../docs/modding-sdk.md): init, validate, pack, install and local repositories.
- [frontier.bounties](samples/frontier.bounties): C# plugin providing the shared bounty catalog.
- [frontier.bounty.dust_bandit](samples/frontier.bounty.dust_bandit): Lua faction contract with no custom assets.

```bash
dotnet build sdk/samples/frontier.bounties/FrontierBounties.csproj -c Release
dotnet run --project tools/Meitou.Tools -- mod validate sdk/samples/frontier.bounties
dotnet run --project tools/Meitou.Tools -- mod pack sdk/samples/frontier.bounties
```

A `.meitoumod` package is a zip of one mod folder; install it into the game's `mods` folder, then enable it in the launcher. Workshop
discovery and remote repositories are later.
