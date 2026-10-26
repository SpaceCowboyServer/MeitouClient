using Meitou.Data.Fcs;

namespace Meitou.Simulation.Saving;

/// <summary>
/// The Meitou contract marker on a character's GAMESTATE_CHARACTER (docs/formats/save.md): the contract whose named target it is. The board stores the same marker
/// on the contract, so an NPC that unloads with its platoon and comes back is still the same target.
/// </summary>
public static class ContractTargetSave
{
    public const string Key = "meitou contract target";

    public static string? Read(FcsRecord? state) =>
        state?.Strings.GetValueOrDefault(Key) is { Length: > 0 } marker ? marker : null;

    public static void Write(FcsRecord state, string? marker)
    {
        if (marker is { Length: > 0 }) state.Strings[Key] = marker;
        else state.Strings.Remove(Key);
    }
}
