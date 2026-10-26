using Meitou.Data;
using Meitou.Data.Save;
using Meitou.Simulation.Bounties;

namespace Meitou.Simulation.Saving;

/// <summary>
/// The bounty board in the CAMERA record: one entry per accepted/completed contract, under Meitou-only keys the original ignores. The faction is stored by its FACTION
/// string id, not its position, so a changed load order resolves the contract again or drops it. Offers are not saved; they are derived from the world on load.
/// </summary>
public static class BountyBoardSave
{
    const string Prefix = "meitou bounty";
    const int MaxEntries = 256;

    public static void Write(SaveCamera camera, World world, PopulationData data)
    {
        var record = camera.Record;
        Clear(record);
        if (world.Systems.OfType<BountyBoardSystem>().FirstOrDefault() is not { } board) return;
        int n = 0;
        foreach (var contract in board.Contracts)
        {
            if (n >= MaxEntries) break;
            if ((uint)contract.Faction >= (uint)data.Factions.Count) continue;
            record.Strings[$"{Prefix}{n} faction"] = data.Factions[contract.Faction].Id;
            record.Ints[$"{Prefix}{n} required"] = contract.Required;
            record.Ints[$"{Prefix}{n} progress"] = contract.Progress;
            record.Ints[$"{Prefix}{n} reward"] = contract.Reward;
            record.Ints[$"{Prefix}{n} state"] = (int)contract.State;
            n++;
        }
        record.Ints[$"{Prefix} count"] = n;
    }

    public static void Read(SaveCamera camera, World world, PopulationData data)
    {
        if (world.Systems.OfType<BountyBoardSystem>().FirstOrDefault() is not { } board) return;
        var record = camera.Record;
        int count = Math.Clamp(record.Ints.GetValueOrDefault($"{Prefix} count"), 0, MaxEntries);
        for (int k = 0; k < count; k++)
        {
            if (!record.Strings.TryGetValue($"{Prefix}{k} faction", out string? factionId) || factionId.Length == 0) continue;
            int faction = data.FactionIndex(factionId);
            if (faction < 0) continue;
            int required = Math.Max(1, record.Ints.GetValueOrDefault($"{Prefix}{k} required", 1));
            int progress = Math.Clamp(record.Ints.GetValueOrDefault($"{Prefix}{k} progress"), 0, required);
            int reward = Math.Max(0, record.Ints.GetValueOrDefault($"{Prefix}{k} reward"));
            bool completed = record.Ints.GetValueOrDefault($"{Prefix}{k} state") == (int)BountyState.Completed;
            board.Restore(faction, required, progress, reward, completed);
        }
        board.PublishForLoad(world);
    }

    static void Clear(Meitou.Data.Fcs.FcsRecord record)
    {
        int count = Math.Clamp(record.Ints.GetValueOrDefault($"{Prefix} count"), 0, MaxEntries);
        for (int k = 0; k < count; k++)
        {
            record.Strings.Remove($"{Prefix}{k} faction");
            record.Ints.Remove($"{Prefix}{k} required");
            record.Ints.Remove($"{Prefix}{k} progress");
            record.Ints.Remove($"{Prefix}{k} reward");
            record.Ints.Remove($"{Prefix}{k} state");
        }
        record.Ints.Remove($"{Prefix} count");
    }
}
