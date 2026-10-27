using Meitou.Modding.Api;
using Meitou.Simulation;
using Meitou.Simulation.Bounties;

namespace Meitou.Modding.Runtime;

/// <summary>
/// Runs after the standard systems and publishes the shared API events. It records the board's first state without replaying accepted events
/// (a save restores contracts before the first tick), then emits accepted/completed events as contracts change. One instance belongs to one world.
/// </summary>
public sealed class ModRuntimeSystem(ModApiRuntime api, BountyBoardSystem? board = null) : ITickSystem
{
    readonly Dictionary<string, BountyState> known = new(StringComparer.Ordinal);
    bool started;

    public void SlowWorld(World world)
    {
        board ??= world.Systems.OfType<BountyBoardSystem>().FirstOrDefault();
        api.AttachBoard(board);
        if (!started)
        {
            started = true;
            api.EventBus.Publish(new WorldStarted(world.Seed, world.Tick, world.Player.Money, world.GameHours));
            Scan(initial: true);
        }
        else
        {
            Scan(initial: false);
        }
        api.EventBus.Publish(new WorldTick(world.Seed, world.Tick, world.Player.Money, world.GameHours));
    }

    void Scan(bool initial)
    {
        if (board is null) return;

        var current = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contract in board.Contracts)
        {
            current.Add(contract.Id);
            if (!known.TryGetValue(contract.Id, out var previous))
            {
                known[contract.Id] = contract.State;
                if (initial) continue;
                if (contract.State == BountyState.Completed) PublishCompleted(contract);
                else PublishAccepted(contract);
                continue;
            }
            if (previous == contract.State) continue;
            known[contract.Id] = contract.State;
            if (contract.State == BountyState.Completed) PublishCompleted(contract);
        }
        foreach (string id in known.Keys.Where(id => !current.Contains(id)).ToList()) known.Remove(id);
    }

    void PublishAccepted(BountyContract contract) => api.EventBus.Publish(new BountyAccepted(
        contract.Id, contract.Title, contract.TargetName, BountyApi.KindName(contract.Kind), BountyApi.ObjectiveName(contract.Objective),
        contract.Required, contract.Reward, contract.Progress));

    void PublishCompleted(BountyContract contract) => api.EventBus.Publish(new BountyCompleted(
        contract.Id, contract.Title, contract.TargetName, BountyApi.KindName(contract.Kind), BountyApi.ObjectiveName(contract.Objective),
        contract.Required, contract.Reward));
}
