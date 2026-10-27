using Meitou.Modding.Api;
using Meitou.Simulation;
using Meitou.Simulation.Bounties;
using Meitou.Simulation.Dialogue;

namespace Meitou.Modding.Runtime;

/// <summary>
/// Runs after the standard systems and publishes the shared API events. It records the board's first state without replaying accepted events
/// (a save restores contracts before the first tick), then emits accepted/completed events as contracts change. One instance belongs to one world.
/// </summary>
public sealed class ModRuntimeSystem(ModApiRuntime api, BountyBoardSystem? board = null) : ITickSystem
{
    readonly Dictionary<string, BountyState> known = new(StringComparer.Ordinal);
    bool started;
    int day;

    public void SlowWorld(World world)
    {
        board ??= world.Systems.OfType<BountyBoardSystem>().FirstOrDefault();
        api.AttachBoard(board);
        api.AttachWorld(world);
        if (world.Systems.OfType<DialogueSystem>().FirstOrDefault() is { } dialogue)
        {
            dialogue.OptionSource = api.Dialogue;
            dialogue.PageSource = api.Dialogue;
        }
        int currentDay = (int)Math.Floor(world.GameHours / 24);
        if (!started)
        {
            started = true;
            day = currentDay;
            api.EventBus.Publish(new WorldStarted(world.Seed, world.Tick, world.Player.Money, world.GameHours));
            Scan(initial: true);
        }
        else
        {
            Scan(initial: false);
            if (currentDay != day)
            {
                day = currentDay;
                api.EventBus.Publish(new WorldDayChanged(currentDay, world.Tick, world.GameHours));
            }
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
            switch (contract.State)
            {
                case BountyState.ReadyToTurnIn: PublishReady(contract); break;
                case BountyState.Completed: PublishCompleted(contract); break;
                case BountyState.Failed: PublishFailed(contract); break;
                case BountyState.Expired: PublishExpired(contract); break;
            }
        }
        foreach (string id in known.Keys.Where(id => !current.Contains(id)).ToList()) known.Remove(id);
    }

    void PublishAccepted(BountyContract contract) => api.EventBus.Publish(new BountyAccepted(
        contract.Id, contract.Title, contract.TargetName, BountyApi.KindName(contract.Kind), BountyApi.ObjectiveName(contract.Objective),
        contract.Required, contract.Reward, contract.Progress, contract.Giver, contract.GiverName));

    void PublishReady(BountyContract contract) => api.EventBus.Publish(new BountyReady(
        contract.Id, contract.Title, contract.TargetName, BountyApi.KindName(contract.Kind), BountyApi.ObjectiveName(contract.Objective),
        contract.Required, contract.Reward, contract.Giver, contract.GiverName));

    void PublishCompleted(BountyContract contract) => api.EventBus.Publish(new BountyCompleted(
        contract.Id, contract.Title, contract.TargetName, BountyApi.KindName(contract.Kind), BountyApi.ObjectiveName(contract.Objective),
        contract.Required, contract.Reward, contract.Giver, contract.GiverName));

    void PublishFailed(BountyContract contract) => api.EventBus.Publish(new BountyFailed(
        contract.Id, contract.Title, contract.TargetName, BountyApi.KindName(contract.Kind), BountyApi.ObjectiveName(contract.Objective),
        contract.Required, contract.Reward, contract.FailureReason, contract.Giver, contract.GiverName));

    void PublishExpired(BountyContract contract) => api.EventBus.Publish(new BountyExpired(
        contract.Id, contract.Title, contract.TargetName, BountyApi.KindName(contract.Kind), BountyApi.ObjectiveName(contract.Objective),
        contract.Required, contract.Reward, contract.FailureReason, contract.Giver, contract.GiverName));
}
