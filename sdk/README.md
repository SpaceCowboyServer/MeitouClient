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

A `.meitoumod` package is a zip of one mod folder; install it from the launcher's **Install mod...** button (or drag it onto the Mods page),
or with `meitou-tools mod install`, then enable it. Workshop discovery and remote repositories are later.
