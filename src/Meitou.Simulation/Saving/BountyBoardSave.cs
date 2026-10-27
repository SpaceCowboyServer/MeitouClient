using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Data.Save;
using Meitou.Simulation.Bounties;

namespace Meitou.Simulation.Saving;

/// <summary>
/// The contract board in the CAMERA record: one entry per open or finished contract under Meitou-only keys the original ignores. The faction is stored by its
/// FACTION string id and a named target by its stable marker; offers are not saved and are derived from the world. Old saves that only held the faction-group
/// fields are read as faction contracts, and missing deadline/giver-detail keys read as no deadline/empty text.
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
            record.Strings[$"{Prefix}{n} id"] = contract.Id;
            record.Ints[$"{Prefix}{n} kind"] = (int)contract.Kind;
            record.Ints[$"{Prefix}{n} objective"] = (int)contract.Objective;
            record.Strings[$"{Prefix}{n} title"] = contract.Title;
            record.Strings[$"{Prefix}{n} name"] = contract.TargetName;
            record.Strings[$"{Prefix}{n} record"] = contract.TargetRecord ?? "";
            record.Strings[$"{Prefix}{n} target"] = contract.TargetMarker ?? "";
            record.Strings[$"{Prefix}{n} faction"] = (uint)contract.Faction < (uint)data.Factions.Count ? data.Factions[contract.Faction].Id : "";
            record.Strings[$"{Prefix}{n} giver"] = contract.Giver;
            record.Strings[$"{Prefix}{n} giver name"] = contract.GiverName;
            record.Strings[$"{Prefix}{n} giver npc"] = contract.GiverNpc;
            record.Strings[$"{Prefix}{n} detail"] = contract.Detail;
            record.Strings[$"{Prefix}{n} reason"] = contract.FailureReason;
            record.Ints[$"{Prefix}{n} required"] = contract.Required;
            record.Ints[$"{Prefix}{n} progress"] = contract.Progress;
            record.Ints[$"{Prefix}{n} reward"] = contract.Reward;
            record.Ints[$"{Prefix}{n} state"] = (int)contract.State;
            record.Floats[$"{Prefix}{n} accepted"] = (float)contract.AcceptedAtGameHours;
            record.Floats[$"{Prefix}{n} expires"] = (float)contract.ExpiresAtGameHours;
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
            string factionId = record.Strings.GetValueOrDefault($"{Prefix}{k} faction", "");
            int faction = data.FactionIndex(factionId);
            var kind = (BountyTargetKind)record.Ints.GetValueOrDefault($"{Prefix}{k} kind", (int)BountyTargetKind.Faction);
            var objective = (BountyObjective)record.Ints.GetValueOrDefault($"{Prefix}{k} objective", (int)BountyObjective.Kill);
            string target = record.Strings.GetValueOrDefault($"{Prefix}{k} target", "");
            string targetRecord = record.Strings.GetValueOrDefault($"{Prefix}{k} record", "");
            string name = record.Strings.GetValueOrDefault($"{Prefix}{k} name", "");
            string title = record.Strings.GetValueOrDefault($"{Prefix}{k} title", "");
            string id = record.Strings.GetValueOrDefault($"{Prefix}{k} id", "");

            if (kind == BountyTargetKind.Faction)
            {
                if (faction < 0) continue;
                name = name.Length > 0 ? name : data.Factions[faction].Name;
                title = title.Length > 0 ? title : name;
                id = id.Length > 0 ? id : data.Factions[faction].Id;
            }
            else if (kind == BountyTargetKind.AnimalSpecies)
            {
                targetRecord = targetRecord.Length > 0 ? targetRecord : target;
                if (targetRecord.Length == 0) continue;
                name = name.Length > 0 ? name : data.Db.Find(targetRecord)?.Name ?? targetRecord;
                title = title.Length > 0 ? title : "Cull " + name;
                id = id.Length > 0 ? id : "cull:" + targetRecord;
            }
            else
            {
                if (target.Length == 0) continue;
                name = name.Length > 0 ? name : targetRecord;
                title = title.Length > 0 ? title : "Hunt " + name;
                id = id.Length > 0 ? id : target;
            }

            int required = Math.Max(1, record.Ints.GetValueOrDefault($"{Prefix}{k} required", 1));
            int progress = Math.Clamp(record.Ints.GetValueOrDefault($"{Prefix}{k} progress"), 0, required);
            int reward = Math.Max(0, record.Ints.GetValueOrDefault($"{Prefix}{k} reward"));
            var state = (BountyState)record.Ints.GetValueOrDefault($"{Prefix}{k} state", (int)BountyState.Accepted);
            if (!Enum.IsDefined(state)) state = BountyState.Accepted;
            string giver = record.Strings.GetValueOrDefault($"{Prefix}{k} giver", "");
            string giverName = record.Strings.GetValueOrDefault($"{Prefix}{k} giver name", "");
            string giverNpc = record.Strings.GetValueOrDefault($"{Prefix}{k} giver npc", "");
            string detail = record.Strings.GetValueOrDefault($"{Prefix}{k} detail", "");
            string reason = record.Strings.GetValueOrDefault($"{Prefix}{k} reason", "");
            double accepted = record.Floats.GetValueOrDefault($"{Prefix}{k} accepted", -1);
            double expires = record.Floats.GetValueOrDefault($"{Prefix}{k} expires", -1);
            board.Restore(id, kind, objective, title, name, targetRecord.Length > 0 ? targetRecord : null, target.Length > 0 ? target : null,
                faction, required, progress, reward, state, giver, detail, giverName, giverNpc, accepted, expires, reason);
        }
        board.PublishForLoad(world);
    }

    static void Clear(FcsRecord record)
    {
        int count = Math.Clamp(record.Ints.GetValueOrDefault($"{Prefix} count"), 0, MaxEntries);
        for (int k = 0; k < count; k++)
        {
            record.Strings.Remove($"{Prefix}{k} id");
            record.Strings.Remove($"{Prefix}{k} title");
            record.Strings.Remove($"{Prefix}{k} name");
            record.Strings.Remove($"{Prefix}{k} record");
            record.Strings.Remove($"{Prefix}{k} target");
            record.Strings.Remove($"{Prefix}{k} faction");
            record.Strings.Remove($"{Prefix}{k} giver");
            record.Strings.Remove($"{Prefix}{k} giver name");
            record.Strings.Remove($"{Prefix}{k} giver npc");
            record.Strings.Remove($"{Prefix}{k} detail");
            record.Strings.Remove($"{Prefix}{k} reason");
            foreach (string key in new[] { "kind", "objective", "required", "progress", "reward", "state" })
                record.Ints.Remove($"{Prefix}{k} {key}");
            record.Floats.Remove($"{Prefix}{k} accepted");
            record.Floats.Remove($"{Prefix}{k} expires");
        }
        record.Ints.Remove($"{Prefix} count");
    }
}
