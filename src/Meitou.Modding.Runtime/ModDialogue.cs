using Meitou.Modding.Api;
using Meitou.Simulation;
using Meitou.Simulation.Dialogue;
using ApiDialoguePage = Meitou.Modding.Api.DialoguePage;
using SimDialoguePage = Meitou.Simulation.Dialogue.DialoguePage;
using SimDialoguePageChoice = Meitou.Simulation.Dialogue.DialoguePageChoice;

namespace Meitou.Modding.Runtime;

/// <summary>The API's dynamic conversation choices, adapted to the dialogue system's option source.</summary>
public sealed class ModDialogue : IDialogue, IDialogueOptionSource, IDialoguePageSource
{
    readonly Action<string>? write;
    readonly object gate = new();
    readonly List<DialogueOption> options = [];
    SimDialoguePage? page;
    Action<string>? pageSelected;

    public ModDialogue(Action<string>? write = null) => this.write = write;

    public IDisposable AddOption(DialogueOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        if (string.IsNullOrWhiteSpace(option.Id)) throw new ArgumentException("A dialogue option needs an ID.", nameof(option));
        ArgumentNullException.ThrowIfNull(option.Text);
        lock (gate)
        {
            if (options.Exists(existing => existing.Id == option.Id))
                throw new InvalidOperationException($"Dialogue option '{option.Id}' is already registered.");
            options.Add(option);
        }
        return new Subscription(this, option);
    }

    public IReadOnlyList<DialogueChoiceOption> Options(World world, DialogueSituation situation)
    {
        DialogueOption[] snapshot;
        lock (gate) snapshot = [.. options];
        var context = Context(situation);
        var result = new List<DialogueChoiceOption>();
        foreach (var option in snapshot)
        {
            try
            {
                if (option.CanShow?.Invoke(context) == false) continue;
                if (option.Text(context) is { Length: > 0 } text) result.Add(new(option.Id, text));
            }
            catch (Exception e)
            {
                write?.Invoke($"mods: dialogue option '{option.Id}' failed: {e.GetBaseException().Message}");
            }
        }
        return result;
    }

    public bool Select(World world, DialogueSituation situation, string optionId)
    {
        DialogueOption? option;
        lock (gate) option = options.FirstOrDefault(existing => existing.Id == optionId);
        if (option is null) return false;
        try { option.Selected?.Invoke(Context(situation)); }
        catch (Exception e)
        {
            write?.Invoke($"mods: dialogue option '{option.Id}' failed: {e.GetBaseException().Message}");
        }
        return true;
    }

    public void AttachWorld(World world) { }

    public void ShowPage(ApiDialoguePage page, Action<string>? selected = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(page.Text);
        var choices = page.Choices?.Select(choice => new SimDialoguePageChoice(choice.Id, choice.Text)).ToArray() ?? [];
        var mapped = new SimDialoguePage(page.Text, choices);
        lock (gate)
        {
            this.page = mapped;
            pageSelected = selected;
        }
    }

    public void ClosePage()
    {
        lock (gate)
        {
            page = null;
            pageSelected = null;
        }
    }

    public SimDialoguePage? Page(World world, DialogueSituation situation)
    {
        lock (gate) return page;
    }

    public bool SelectPage(World world, DialogueSituation situation, string optionId)
    {
        Action<string>? callback;
        lock (gate) callback = pageSelected;
        if (callback is null) return false;
        try { callback(optionId); }
        catch (Exception e) { write?.Invoke($"mods: dialogue page failed: {e.GetBaseException().Message}"); }
        return true;
    }

    static DialogueContext Context(DialogueSituation situation) => new(
        situation.Npc.ToString(), situation.NpcName, situation.NpcRecord, situation.TownId, situation.TownName,
        situation.Day, situation.IsTrader, situation.IsBartender, situation.AtRoot, situation.GameHours);

    void Remove(DialogueOption option)
    {
        lock (gate) options.Remove(option);
    }

    sealed class Subscription(ModDialogue owner, DialogueOption option) : IDisposable
    {
        int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            owner.Remove(option);
        }
    }
}
