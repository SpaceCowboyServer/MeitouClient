namespace Meitou.Simulation.Dialogue;

/// <summary>Where a player conversation is, for a mod's dynamic choices (the game-side adapter maps it to the public API).</summary>
public sealed record DialogueSituation(
    CharacterId Npc, CharacterId Player, string NpcName, string NpcRecord,
    string TownId, string TownName, int Day, bool IsTrader, bool IsBartender, bool AtRoot,
    double GameHours = -1)
{
    public string GiverId => (TownId.Length > 0 ? TownId : "unknown") + ":" + NpcRecord;
}

/// <summary>One extra choice a mod offers in a conversation.</summary>
public sealed record DialogueChoiceOption(string Id, string Text);

/// <summary>A host-rendered conversation page: a block of text and its choices, above the data choices.</summary>
public sealed record DialoguePage(string Text, IReadOnlyList<DialoguePageChoice> Choices);
public sealed record DialoguePageChoice(string Id, string Text);

/// <summary>Optional host pages layered over a conversation (the mod dialogue adapter implements it).</summary>
public interface IDialoguePageSource
{
    DialoguePage? Page(World world, DialogueSituation situation);
    bool SelectPage(World world, DialogueSituation situation, string optionId);
    void ClosePage();
}

/// <summary>
/// The host's dynamic dialogue choices. <see cref="DialogueSystem"/> appends them after the data's choices; selecting one calls
/// <see cref="Select"/> and then rebuilds the choices, so an accepted/turned-in contract shows its new state immediately.
/// </summary>
public interface IDialogueOptionSource
{
    IReadOnlyList<DialogueChoiceOption> Options(World world, DialogueSituation situation);
    bool Select(World world, DialogueSituation situation, string optionId);
}
