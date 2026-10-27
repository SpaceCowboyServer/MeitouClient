using Meitou.Modding.Api;
using Meitou.Simulation;
using Meitou.Simulation.Bounties;
using ApiBountyContractView = Meitou.Modding.Api.BountyContractView;
using SimBountyContractView = Meitou.Simulation.Bounties.BountyContractView;

namespace Meitou.Modding.Runtime;

/// <summary>
/// The game-side adapter between <see cref="BountyContractCatalog"/> and the stable API DTOs. Registration maps API targets and goals onto
/// the simulation's definitions; the board view maps published offers and active contracts back to API types.
/// </summary>
public sealed class BountyApi : IBountyBoard
{
    readonly BountyContractCatalog catalog;
    BountyBoardSystem? board;
    World? world;

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
                offer.Id, offer.Title, offer.Detail, offer.Required, offer.Reward, offer.Distance, offer.DeadlineHours))];
        }
    }

    public IReadOnlyList<ApiBountyContractView> Active
    {
        get
        {
            var view = board?.Published;
            if (view is null) return [];
            return [.. view.Active.Select(Map)];
        }
    }

    public IReadOnlyList<BountyOffer> OffersFor(string giver)
    {
        if (string.IsNullOrEmpty(giver)) return [];
        var view = board?.PublishedForGiver(giver) ?? [];
        return [.. view.Select(offer => new BountyOffer(offer.Id, offer.Title, offer.Detail, offer.Required, offer.Reward, offer.Distance, offer.DeadlineHours))];
    }

    public IReadOnlyList<ApiBountyContractView> Journal
    {
        get
        {
            var view = board?.Published;
            return view is null ? [] : [.. view.Journal.Select(Map)];
        }
    }

    public IReadOnlyList<ApiBountyContractView> ActiveFor(string giver) =>
        string.IsNullOrEmpty(giver) || board is null ? [] : [.. board.Contracts.Where(contract => contract.Giver == giver && contract.State.IsOpen())
            .Select(Map)];

    public IReadOnlyList<ApiBountyContractView> CompletedFor(string giver) =>
        string.IsNullOrEmpty(giver) || board is null ? [] : [.. board.Contracts.Where(contract => contract.Giver == giver && contract.State == BountyState.Completed)
            .Select(Map)];

    public bool Accept(string offerId, string giverNpc = "", string giverName = "") =>
        board is not null && world is not null && board.TryAccept(world, offerId, giverNpc, giverName);
    public bool TurnIn(string contractId) => board is not null && world is not null && board.TurnIn(world, contractId);
    public bool Abandon(string contractId) => board is not null && world is not null && board.TryAbandon(world, contractId);

    public void Refresh()
    {
        if (board is not null && world is not null) board.Refresh(world);
    }

    /// <summary>Points the board view at the world's board; a new world replaces it.</summary>
    public void Attach(BountyBoardSystem? value) => board = value;

    public void AttachWorld(World? value) => world = value;

    static ApiBountyContractView Map(SimBountyContractView contract) =>
        new(contract.Id, contract.Title, contract.Progress, contract.Required, contract.Reward, contract.Distance, contract.Ready, contract.Giver,
            contract.Target, contract.Detail, contract.GiverName, Status(contract.State), contract.AcceptedAtGameHours, contract.ExpiresAtGameHours);

    static ApiBountyContractView Map(BountyContract contract) =>
        new(contract.Id, contract.Title, contract.Progress, contract.Required, contract.Reward, 0,
            contract.State == BountyState.ReadyToTurnIn, contract.Giver, contract.TargetName, contract.Detail, contract.GiverName,
            Status(contract.State), contract.AcceptedAtGameHours, contract.ExpiresAtGameHours);

    static BountyStatus Status(BountyState state) => state switch
    {
        BountyState.ReadyToTurnIn => BountyStatus.ReadyToTurnIn,
        BountyState.Completed => BountyStatus.Completed,
        BountyState.Failed => BountyStatus.Failed,
        BountyState.Expired => BountyStatus.Expired,
        _ => BountyStatus.Accepted,
    };

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
            BountyTarget.Character => BountyTargetKind.Character,
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
            Giver = definition.Giver ?? "",
            DeadlineHours = definition.DeadlineHours,
            RewardMax = definition.RewardMax,
            RequiredFactionId = definition.RequiredFactionId ?? "",
            MinRelation = definition.MinRelation,
            MinRank = definition.MinRank,
            RequiredCompletedContractId = definition.RequiredCompletedContractId ?? "",
            RelationOnComplete = definition.RelationOnComplete,
            RelationOnFailure = definition.RelationOnFailure,
        });
        return id;
    }

    public bool Remove(string source, string id) => catalog.Remove(source, id);

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
    public bool Remove(string id) => api.Remove(modId, id);
}
