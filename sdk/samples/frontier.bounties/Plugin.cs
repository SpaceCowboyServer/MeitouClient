using Meitou.Modding.Api;

namespace Frontier.Bounties;

/// <summary>Content pack: three Dust Bandit contract templates for every bartender.</summary>
public sealed class Plugin : IMod
{
    public void Initialize(IModContext context)
    {
        var framework = context.Services.Get<IContractFramework>(ModServiceKeys.Contracts)
            ?? throw new InvalidOperationException("frontier.contracts is required but did not provide its framework service.");
        framework.RegisterTemplate(Template(
            "frontier.bounties.dust-bandit-patrol",
            "Dust Bandit Patrol",
            "One Dust Bandit kill for the frontier board.",
            required: 1,
            reward: 1500));
        framework.RegisterTemplate(CharacterTemplate(
            "frontier.bounties.dust-bandit-hunt",
            "Dust Bandit Hunt",
            "Track down a Dust Bandit for the frontier board.",
            reward: 3000));
        framework.RegisterTemplate(Template(
            "frontier.bounties.dust-bandit-contract",
            "Dust Bandit Contract",
            "Three Dust Bandit kills for the frontier board.",
            required: 3,
            reward: 4500));
        context.Log.Info("registered 3 Dust Bandit contract templates");
    }

    static ContractTemplate Template(string id, string title, string detail, int required, int reward) =>
        new(new BountyDefinition(
            LocalId: id,
            Target: BountyTarget.Faction,
            Goal: BountyGoal.Kill,
            Title: title,
            Detail: detail,
            FactionId: "200-gamedata.base",
            Required: required,
            Reward: reward,
            DeadlineHours: 72));

    static ContractTemplate CharacterTemplate(string id, string title, string detail, int reward) =>
        new(new BountyDefinition(
            LocalId: id,
            Target: BountyTarget.Character,
            Goal: BountyGoal.Kill,
            Title: title,
            Detail: detail,
            FactionId: "200-gamedata.base",
            Required: 1,
            Reward: reward,
            DeadlineHours: 72));
}
