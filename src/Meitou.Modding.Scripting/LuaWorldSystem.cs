using Meitou.Simulation;
using Meitou.Simulation.Bounties;
using MoonSharp.Interpreter;

namespace Meitou.Modding.Scripting;

/// <summary>
/// The per-world bridge for Lua callbacks. It runs after the standard systems, so the contract board has already accepted, completed or republished its
/// contracts for the tick. Existing contracts of a loaded world are remembered without firing synthetic accept events.
/// </summary>
sealed class LuaWorldSystem(LuaModHost host) : ITickSystem
{
    readonly Dictionary<string, BountyState> known = new(StringComparer.Ordinal);
    bool started;

    public void SlowWorld(World world)
    {
        if (!started)
        {
            started = true;
            host.FireAll("world.start", table => FillWorld(table, world));
            Scan(world, initial: true);
        }
        else
        {
            Scan(world, initial: false);
        }
        host.FireAll("world.tick", table => FillWorld(table, world));
    }

    void Scan(World world, bool initial)
    {
        if (world.Systems.OfType<BountyBoardSystem>().FirstOrDefault() is not { } board) return;
        var current = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contract in board.Contracts)
        {
            current.Add(contract.Id);
            if (!known.TryGetValue(contract.Id, out var previous))
            {
                known[contract.Id] = contract.State;
                if (!initial)
                {
                    if (contract.State == BountyState.Completed) host.FireAll("bounty.completed", table => FillBounty(table, contract));
                    else host.FireAll("bounty.accepted", table => FillBounty(table, contract));
                }
                continue;
            }
            if (previous == contract.State) continue;
            known[contract.Id] = contract.State;
            if (contract.State == BountyState.Completed) host.FireAll("bounty.completed", table => FillBounty(table, contract));
        }
        foreach (string id in known.Keys.Where(id => !current.Contains(id)).ToList()) known.Remove(id);
    }

    static void FillWorld(Table table, World world)
    {
        table["tick"] = world.Tick;
        table["seed"] = world.Seed.ToString("x16");
        table["money"] = world.Player.Money;
        table["game_hours"] = world.GameHours;
    }

    static void FillBounty(Table table, BountyContract contract)
    {
        table["id"] = contract.Id;
        table["title"] = contract.Title;
        table["target"] = contract.TargetName;
        table["kind"] = contract.Kind switch
        {
            BountyTargetKind.Faction => "faction",
            BountyTargetKind.AnimalSpecies => "animal",
            BountyTargetKind.Spawned => "spawned",
            _ => "character",
        };
        table["objective"] = contract.Objective == BountyObjective.Capture ? "capture" : "kill";
        table["required"] = contract.Required;
        table["reward"] = contract.Reward;
        table["progress"] = contract.Progress;
    }
}
