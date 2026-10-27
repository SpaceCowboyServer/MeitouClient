using Meitou.Modding.Api;
using Meitou.Simulation.Bounties;
using ApiBountyContractView = Meitou.Modding.Api.BountyContractView;

namespace Meitou.Modding.Runtime;

/// <summary>
/// The game-side adapter between <see cref="BountyContractCatalog"/> and the stable API DTOs. Registration maps API targets and goals onto
/// the simulation's definitions; the board view maps published offers and active contracts back to API types.
/// </summary>
public sealed class BountyApi : IBountyBoard
{
    readonly BountyContractCatalog catalog;
    BountyBoardSystem? board;

    public BountyApi(BountyContractCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
    }

    public IReadOnlyList<BountyOffer> Offers
    {
        get
        {
            var view = board?.Published;
            if (view is null) return [];
            return [.. view.Offers.Select(offer => new BountyOffer(
                offer.Id, offer.Title, offer.Detail, offer.Required, offer.Reward, offer.Distance))];
        }
    }

    public IReadOnlyList<ApiBountyContractView> Active
    {
        get
        {
            var view = board?.Published;
            if (view is null) return [];
            return [.. view.Active.Select(contract => new ApiBountyContractView(
                contract.Id, contract.Title, contract.Progress, contract.Required, contract.Reward, contract.Distance))];
        }
    }

    /// <summary>Points the board view at the world's board; a new world replaces it.</summary>
    public void Attach(BountyBoardSystem? value) => board = value;

    /// <summary>Registers one mod definition and returns its canonical ID.</summary>
    public string Register(string source, BountyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!ModId.IsValid(source)) throw new ArgumentException($"'{source}' is not a mod ID.", nameof(source));

        string localId = definition.LocalId;
        if (string.IsNullOrWhiteSpace(localId) || localId.Length > 64 ||
            localId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.')))
            throw new ArgumentException("A contract local ID must use 1-64 letters, digits, '_', '-' or '.'.", nameof(definition));

        if (!Enum.IsDefined(definition.Target))
            throw new ArgumentException("The contract has an unknown target.", nameof(definition));
        if (!Enum.IsDefined(definition.Goal))
            throw new ArgumentException("The contract has an unknown goal.", nameof(definition));

        var kind = definition.Target switch
        {
            BountyTarget.Faction => BountyTargetKind.Faction,
            BountyTarget.AnimalSpecies => BountyTargetKind.AnimalSpecies,
            BountyTarget.Spawned => BountyTargetKind.Spawned,
            _ => throw new ArgumentException("The contract has an unknown target.", nameof(definition)),
        };
        var objective = definition.Goal == BountyGoal.Capture ? BountyObjective.Capture : BountyObjective.Kill;
        string id = $"{source}:bounty/{localId}";
        catalog.Register(source, new BountyContractDefinition
        {
            Id = id,
            Kind = kind,
            Objective = objective,
            Title = definition.Title,
            Detail = definition.Detail,
            TargetRecord = definition.TargetRecord,
            FactionId = definition.FactionId,
            Required = definition.Required,
            Reward = definition.Reward,
        });
        return id;
    }

    public int RemoveSource(string source) => catalog.RemoveSource(source);

    internal static string KindName(BountyTargetKind kind) => kind switch
    {
        BountyTargetKind.Faction => "faction",
        BountyTargetKind.AnimalSpecies => "animal",
        BountyTargetKind.Spawned => "spawned",
        _ => "character",
    };

    internal static string ObjectiveName(BountyObjective objective) =>
        objective == BountyObjective.Capture ? "capture" : "kill";
}

/// <summary>A registry scoped to one mod, so local IDs are namespaced without the mod spelling its own prefix.</summary>
sealed class ScopedBountyContracts(BountyApi api, string modId) : IBountyContracts
{
    public string Register(BountyDefinition definition) => api.Register(modId, definition);
}
