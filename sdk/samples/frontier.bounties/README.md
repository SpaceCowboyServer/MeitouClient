# frontier.bounties

The sample C# bounty framework. It registers a Dust Bandits contract through `IModContext.Bounties`, subscribes to the shared
`BountyCompleted` and `WorldTick` events, and keeps a small `ISaveable` heartbeat so loading it exercises the sidecar path. The
game's one catalog is shared with Lua content such as [frontier.bounty.dust_bandit](../frontier.bounty.dust_bandit).

## Build and pack

From the repository root:

```bash
dotnet build sdk/samples/frontier.bounties/FrontierBounties.csproj -c Release
dotnet run --project tools/Meitou.Tools -- mod pack sdk/samples/frontier.bounties --out dist/frontier.bounties-1.0.0.meitoumod
```

The project copies `lib/FrontierBounties.dll` after a build (the file is git-ignored). `mod pack` refuses to package a plugin whose assembly is
missing, so build before packing. The generated package contains an empty `.mod`; copy it to a local repository or install it with
`meitou-tools mod install` and enable it in the launcher.

`frontier.bounty.dust_bandit` depends on this mod. See [modding-csharp](../../../docs/modding-csharp.md) and
[modding-api](../../../docs/modding-api.md) for the contracts.
