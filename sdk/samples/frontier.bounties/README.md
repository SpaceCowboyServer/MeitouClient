# frontier.bounties

The sample C# bounty framework. It provides the shared `BountyContractCatalog` under `ModServiceKeys.BountyContracts`; the game passes that
catalog to the bounty board and to the Lua host, so Lua content such as [frontier.bounty.dust_bandit](../frontier.bounty.dust_bandit) adds offers
to the same board. It also registers an `ITickSystem` and an `ISaveable`, so loading it exercises the plugin system and sidecar paths.

## Build and pack

From the repository root:

```bash
dotnet build sdk/samples/frontier.bounties/FrontierBounties.csproj -c Release
dotnet run --project tools/Meitou.Tools -- mod pack sdk/samples/frontier.bounties --out dist/frontier.bounties-1.0.0.meitoumod
```

The project copies `lib/FrontierBounties.dll` after a build (the file is git-ignored). `mod pack` refuses to package a plugin whose assembly is
missing, so build before packing. The generated package contains an empty `.mod`; copy it to a local repository or install it with
`meitou-tools mod install` and enable it in the launcher.

`frontier.bounty.dust_bandit` depends on this mod. See [modding-csharp](../../../docs/modding-csharp.md) for the plugin contract.
