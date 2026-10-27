# frontier.bounty.dust_bandit

Sample Lua content for the frontier bounty board. It depends on [frontier.bounties](../frontier.bounties) and its
[frontier.contracts](../frontier.contracts) foundation, registers a global faction contract for `Dust Bandits`, declares the
`events:world`, `events:bounty` and `contracts:write` permissions, and logs accepted/ready/completed/failed/expired events and `day.changed`. It has no
custom assets, so it works against base-game data.

The bartender-specific Dust Bandits batch and dialogue are provided by the C# framework and content samples; this script demonstrates
the shared Lua contract and event API alongside it.

```bash
dotnet run --project tools/Meitou.Tools -- mod validate sdk/samples/frontier.bounty.dust_bandit
dotnet run --project tools/Meitou.Tools -- mod pack sdk/samples/frontier.bounty.dust_bandit --out dist/frontier.bounty.dust_bandit-1.0.0.meitoumod
```

Install the package with `meitou-tools mod install`, put the sample packages in a local repository, and enable
`frontier.contracts`, `frontier.bounties` and `frontier.bounty.dust_bandit` in the launcher. The dependencies are resolved by the
launcher; a missing or disabled framework refuses the launch with a clear message.
