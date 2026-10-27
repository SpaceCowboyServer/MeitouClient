# frontier.bounties

Content pack for the reusable [frontier.contracts](../frontier.contracts) framework. It registers three Dust Bandit
contract templates for every bartender:

- Dust Bandit Patrol: one kill, c.1,500, 72-hour deadline
- Dust Bandit Hunt: two kills, c.3,000, 72-hour deadline
- Dust Bandit Contract: three kills, c.4,500, 72-hour deadline

The framework handles the bartender dialogue, giver slots, daily replacement, turn-in and save state. This mod only
declares the dependency and supplies content; it has no systems or save data of its own.

```json
"depends": { "frontier.contracts": ">=1.0 <2" }
```

## Build and pack

From the repository root:

```bash
dotnet build sdk/samples/frontier.bounties/FrontierBounties.csproj -c Release
dotnet run --project tools/Meitou.Tools -- mod pack sdk/samples/frontier.bounties --out dist/frontier.bounties-1.0.0.meitoumod
```

The project copies `lib/FrontierBounties.dll` after a build (the file is git-ignored). Build before packing. Install the
package with `meitou-tools mod install` or a local repository, then enable both `frontier.contracts` and
`frontier.bounties` in the launcher.

The Lua sample [frontier.bounty.dust_bandit](../frontier.bounty.dust_bandit) depends on this content pack and still uses the
engine's raw contract API for its global demonstration contract.
