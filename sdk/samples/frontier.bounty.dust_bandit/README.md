# frontier.bounty.dust_bandit

Sample Lua content for the frontier bounty board. It depends on [frontier.bounties](../frontier.bounties), registers a faction contract for
`Dust Bandits`, and logs accepted/completed events. It has no custom assets, so it works against base-game data.

```bash
dotnet run --project tools/Meitou.Tools -- mod validate sdk/samples/frontier.bounty.dust_bandit
dotnet run --project tools/Meitou.Tools -- mod pack sdk/samples/frontier.bounty.dust_bandit --out dist/frontier.bounty.dust_bandit-1.0.0.meitoumod
```

Install the package with `meitou-tools mod install`, put both sample packages in a local repository, and enable `frontier.bounties` then
`frontier.bounty.dust_bandit` in the launcher. The script's dependency is resolved by the launcher; a missing or disabled framework refuses
the launch with a clear message.
