# frontier.contracts

Foundation plugin for reusable giver contracts. It owns the content-neutral behavior:

- registering contract templates through `IContractFramework`
- three slots per bartender
- dialogue options for asking, taking and turning in
- daily replacement of finished slots
- deadlines, giver-death failure and the journal's open giver contracts
- save state for generated giver slots

It does **not** contain Dust Bandit content. That belongs to a content pack such as
[frontier.bounties](../frontier.bounties), which depends on this mod through the `frontier.contracts` capability.

## Contract for content mods

Reference `Meitou.Modding.Api`, declare the dependency, and register templates during `Initialize`:

```csharp
var framework = context.Services.Get<IContractFramework>(ModServiceKeys.Contracts)
    ?? throw new InvalidOperationException("frontier.contracts is required.");

framework.RegisterTemplate(new ContractTemplate(new BountyDefinition(
    LocalId: "example.dust-patrol",
    Target: BountyTarget.Faction,
    Goal: BountyGoal.Kill,
    Title: "Dust Bandit Patrol",
    Detail: "One Dust Bandit kill.",
    FactionId: "200-gamedata.base",
    Required: 1,
    Reward: 1500)));
```

`GiverRole` defaults to `bartender`; `TownId`/`TownName` can restrict a template to one town. `BountyDefinition.DeadlineHours`
defaults to 0 (no deadline). `BountyTarget.Character` selects one nearby non-unique character by `FactionId`, `TargetRecord`, or both;
its `Required` is always one and the board marks the chosen character when the contract is accepted. Template IDs are globally unique. The framework creates the actual per-giver IDs and stores the
giver's slots and character id in its own save sidecar. The engine fails the contract if the giver dies while loaded and lists
open giver jobs in the Faction tab journal with a detail panel.

## Build and pack

```bash
dotnet build sdk/samples/frontier.contracts/FrontierContracts.csproj -c Release
dotnet run --project tools/Meitou.Tools -- mod pack sdk/samples/frontier.contracts --out dist/frontier.contracts-1.0.0.meitoumod
```

The manifest provides `frontier.contracts@1`. Content packs use:

```json
"depends": { "frontier.contracts": ">=1.0 <2" }
```

The current C# service is the supported cross-mod route. Lua content can still register raw engine contracts through
`meitou.contracts`; a language-neutral template bridge is future work.
