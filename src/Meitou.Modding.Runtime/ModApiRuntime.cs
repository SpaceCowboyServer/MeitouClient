using Meitou.Modding.Api;
using Meitou.Simulation.Bounties;

namespace Meitou.Modding.Runtime;

/// <summary>Owns the one event bus and bounty adapter shared by the C# and Lua hosts of a game session.</summary>
public sealed class ModApiRuntime : IModApiHost
{
    public ModApiRuntime(Action<string>? write = null)
        : this(new ModEventBus(write), new BountyContractCatalog())
    {
    }

    public ModApiRuntime(ModEventBus events, BountyContractCatalog contracts)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(contracts);
        EventBus = events;
        Contracts = contracts;
        Bounties = new BountyApi(contracts);
    }

    public ModEventBus EventBus { get; }
    public BountyContractCatalog Contracts { get; }
    public BountyApi Bounties { get; }

    public IModEvents Events => EventBus;
    public IBountyBoard Board => Bounties;

    public IBountyContracts BountiesFor(string modId)
    {
        if (!Meitou.Modding.ModId.IsValid(modId)) throw new ArgumentException($"'{modId}' is not a mod ID.", nameof(modId));
        return new ScopedBountyContracts(Bounties, modId);
    }

    public void RemoveBounties(string modId) => Bounties.RemoveSource(modId);

    /// <summary>Points the board view at the current world's board.</summary>
    public void AttachBoard(BountyBoardSystem? board) => Bounties.Attach(board);
}
