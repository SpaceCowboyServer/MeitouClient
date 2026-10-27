using System.Text;
using Meitou.Modding.Api;

namespace Frontier.Contracts;

/// <summary>
/// Reusable bartender contract framework. Content mods register templates through <see cref="IContractFramework"/>; the framework
/// creates per-giver instances, shows them in the bartender dialogue, and replaces finished slots on the next day.
/// </summary>
public sealed class Plugin : IMod, IContractFramework
{
    readonly object gate = new();
    readonly List<ContractTemplate> templates = [];
    readonly Dictionary<string, ContractTemplate> byId = new(StringComparer.Ordinal);

    public void Initialize(IModContext context)
    {
        context.Services.Provide(ModServiceKeys.Contracts, this);
        var book = new ContractBook(context, this);
        context.Dialogue.AddOption(new DialogueOption(
            "frontier.contracts:ask",
            book.AskText,
            book.AskVisible,
            book.AskSelected));
        for (int i = 0; i < ContractBook.Slots; i++)
        {
            int slot = i;
            context.Dialogue.AddOption(new DialogueOption(
                "frontier.contracts:slot-" + i,
                ctx => book.Describe(ctx, slot),
                ctx => ctx.IsBartender && ctx.AtRoot,
                ctx => book.Select(ctx, slot)));
        }
        context.Events.Subscribe<WorldDayChanged>(day => book.DayChanged(day.Day));
        context.Events.Subscribe<BountyCompleted>(bounty => context.Log.Info($"completed {bounty.Title} for {bounty.Reward} cats"));
        context.AddSaveable(book);
        context.Log.Info("contract-giver framework ready");
    }

    public void RegisterTemplate(ContractTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(template.Definition);
        var definition = template.Definition;
        if (string.IsNullOrWhiteSpace(definition.LocalId) || definition.LocalId.Length > 128 || definition.LocalId.Any(char.IsWhiteSpace))
            throw new ArgumentException("A contract template needs a short ID without whitespace.", nameof(template));
        if (string.IsNullOrWhiteSpace(definition.Title))
            throw new ArgumentException("A contract template needs a title.", nameof(template));
        if (string.IsNullOrWhiteSpace(template.GiverRole))
            throw new ArgumentException("A contract template needs a giver role.", nameof(template));
        if (template.TownId is null || template.TownName is null || template.Tag is null)
            throw new ArgumentException("A contract template's filters cannot be null.", nameof(template));
        if (template.Weight < 1)
            throw new ArgumentException("A contract template's weight must be at least 1.", nameof(template));
        if (template.CooldownDays < 0)
            throw new ArgumentException("A contract template's cooldown cannot be negative.", nameof(template));
        lock (gate)
        {
            if (!byId.TryAdd(definition.LocalId, template))
                throw new InvalidOperationException($"Contract template '{definition.LocalId}' is already registered.");
            templates.Add(template);
        }
    }

    internal IReadOnlyList<ContractTemplate> TemplatesFor(bool isBartender, string townId, string townName)
    {
        lock (gate)
            return [.. templates.Where(template =>
                ((template.GiverRole.Equals("bartender", StringComparison.OrdinalIgnoreCase) && isBartender) || template.GiverRole == "*") &&
                (template.TownId.Length == 0 || template.TownId == townId) &&
                (template.TownName.Length == 0 || template.TownName.Equals(townName, StringComparison.OrdinalIgnoreCase)))];
    }

    internal ContractTemplate? TemplateById(string id)
    {
        lock (gate) return byId.GetValueOrDefault(id);
    }
}

sealed class ContractBook(IModContext context, Plugin owner) : ISaveable
{
    public const int Slots = 3;
    const int Version = 2;

    readonly Dictionary<string, GiverState> givers = new(StringComparer.Ordinal);
    int nextSerial;

    public string Key => "contracts";

    public bool AskVisible(DialogueContext ctx) =>
        ctx.IsBartender && ctx.AtRoot && !givers.ContainsKey(ctx.GiverId) &&
        owner.TemplatesFor(isBartender: true, ctx.TownId, ctx.TownName).Count > 0;

    public string? AskText(DialogueContext ctx) => AskVisible(ctx) ? "Ask about work" : null;

    public void AskSelected(DialogueContext ctx)
    {
        if (!AskVisible(ctx)) return;
        var available = owner.TemplatesFor(isBartender: true, ctx.TownId, ctx.TownName);
        if (available.Count == 0) return;
        var state = new GiverState
        {
            TownId = ctx.TownId,
            TownName = ctx.TownName,
            Day = ctx.Day,
            Slots = new SlotState?[Slots],
        };
        givers[ctx.GiverId] = state;
        FillFreeSlots(ctx.GiverId, state, available);
        if (state.Slots.All(slot => slot is null))
        {
            givers.Remove(ctx.GiverId);
            return;
        }
        context.Board.Refresh();
    }

    public string? Describe(DialogueContext ctx, int slot)
    {
        if (!ctx.IsBartender || !ctx.AtRoot || !givers.TryGetValue(ctx.GiverId, out var state) || (uint)slot >= Slots ||
            state.Slots[slot] is not { } item) return null;
        string id = ContractId(item.LocalId);
        if (context.Board.ActiveFor(ctx.GiverId).FirstOrDefault(contract => contract.Id == id) is { } active)
            return active.Ready ? $"Details: {active.Title} (ready)" : $"Details: {active.Title} ({active.Progress}/{active.Required})";
        if (context.Board.OffersFor(ctx.GiverId).FirstOrDefault(offer => offer.Id == id) is { } offer) return $"Details: {offer.Title}";
        if (context.Board.CompletedFor(ctx.GiverId).FirstOrDefault(contract => contract.Id == id) is { } completed)
            return $"Details: {completed.Title} (completed)";
        return null;
    }

    public void Select(DialogueContext ctx, int slot)
    {
        if (!ctx.IsBartender || !givers.TryGetValue(ctx.GiverId, out var state) || (uint)slot >= Slots ||
            state.Slots[slot] is not { } item) return;
        string id = ContractId(item.LocalId);
        if (context.Board.ActiveFor(ctx.GiverId).FirstOrDefault(contract => contract.Id == id) is { } active)
        {
            ShowPage(ctx, id, active.Title, active.Target, active.GiverName, active.Progress, active.Required,
                active.Reward, active.ExpiresAtGameHours < 0 ? "None" : $"{(active.ExpiresAtGameHours <= ctx.GameHours ? "Due" : $"{Math.Ceiling(active.ExpiresAtGameHours - ctx.GameHours)} h")}",
                active.Ready ? "Ready to turn in" : "Active", canAccept: false, canTurnIn: active.Ready);
            return;
        }
        if (context.Board.OffersFor(ctx.GiverId).FirstOrDefault(offer => offer.Id == id) is { } offer)
        {
            ShowPage(ctx, id, offer.Title, offer.Target, ctx.NpcName, 0, offer.Required, offer.Reward,
                offer.DeadlineHours > 0 ? $"{offer.DeadlineHours} h" : "None", "Available", canAccept: true, canTurnIn: false);
            return;
        }
        if (context.Board.CompletedFor(ctx.GiverId).FirstOrDefault(contract => contract.Id == id) is { } completed)
            ShowPage(ctx, id, completed.Title, completed.Target, completed.GiverName, completed.Progress, completed.Required,
                completed.Reward, "Met", "Completed", canAccept: false, canTurnIn: false);
    }

    void ShowPage(DialogueContext ctx, string id, string title, string target, string giver, int progress, int required,
        int reward, string deadline, string status, bool canAccept, bool canTurnIn)
    {
        var choices = new List<DialoguePageChoice>();
        if (canAccept) choices.Add(new("accept", "Accept contract"));
        if (canTurnIn) choices.Add(new("turnin", "Turn in contract"));
        choices.Add(new("back", "Back"));
        // Keep this a compact brief that fits the normal conversation panel; the journal carries the full description.
        string text = $"{title} — {status}\nTarget: {target}    Giver: {(giver.Length > 0 ? giver : ctx.NpcName)}\nReward: c.{reward:N0}    Deadline: {deadline}\nProgress: {progress}/{required}";
        context.Dialogue.ShowPage(new DialoguePage(text, choices), choice =>
        {
            try
            {
                if (choice == "accept") context.Board.Accept(id, ctx.NpcId, ctx.NpcName);
                else if (choice == "turnin") context.Board.TurnIn(id);
            }
            finally
            {
                context.Dialogue.ClosePage();
            }
        });
    }

    public void DayChanged(int day)
    {
        foreach (string giver in givers.Keys.ToArray())
            RefreshGiver(giver, day);
        context.Board.Refresh();
    }

    void RefreshGiver(string giver, int day)
    {
        if (!givers.TryGetValue(giver, out var state)) return;
        state.Day = day;
        var available = owner.TemplatesFor(isBartender: true, state.TownId, state.TownName);
        for (int slot = 0; slot < Slots; slot++)
        {
            if (state.Slots[slot] is not { } item) continue;
            string id = ContractId(item.LocalId);
            if (context.Board.ActiveFor(giver).Any(contract => contract.Id == id)) continue;
            if (context.Board.OffersFor(giver).Any(offer => offer.Id == id)) continue;
            // A turned-in template waits out its cooldown before it can enter a later slate.
            if (context.Board.CompletedFor(giver).Any(contract => contract.Id == id) &&
                owner.TemplateById(item.TemplateId) is { CooldownDays: > 0 } template)
                state.Cooldowns[item.TemplateId] = Math.Max(state.Cooldowns.GetValueOrDefault(item.TemplateId), day + template.CooldownDays);
            // Finished, abandoned or absent: replace this slot with a new contract for the next day.
            context.Bounties.Remove(id);
            state.Slots[slot] = null;
        }
        FillFreeSlots(giver, state, available);
    }

    void FillFreeSlots(string giver, GiverState state, IReadOnlyList<ContractTemplate> available)
    {
        var eligible = available.Where(template =>
            !state.Cooldowns.TryGetValue(template.Definition.LocalId, out int until) || until <= state.Day).ToArray();
        if (eligible.Length == 0) return;
        var used = new HashSet<string>(state.Slots.Where(slot => slot is not null).Select(slot => slot!.TemplateId), StringComparer.Ordinal);
        var usedTags = new HashSet<string>(state.Slots.Where(slot => slot is not null)
            .Select(slot => owner.TemplateById(slot!.TemplateId)?.Tag ?? "").Where(tag => tag.Length > 0), StringComparer.Ordinal);
        var ordered = eligible
            .OrderByDescending(template => WeightedKey(giver, state.Day, template))
            .ToArray();
        for (int slot = 0; slot < Slots; slot++)
        {
            if (state.Slots[slot] is not null) continue;
            foreach (var template in ordered)
            {
                string templateId = template.Definition.LocalId;
                if (used.Contains(templateId)) continue;
                if (template.Tag.Length > 0 && usedTags.Contains(template.Tag)) continue;
                string local = $"contract-{nextSerial++}";
                if (!TryRegister(giver, local, template)) continue;
                state.Slots[slot] = new SlotState(local, templateId);
                used.Add(templateId);
                if (template.Tag.Length > 0) usedTags.Add(template.Tag);
                break;
            }
        }
    }

    /// <summary>Efraimidis-Spirakis weighted sampling without replacement, keyed deterministically by giver and day.</summary>
    double WeightedKey(string giver, int day, ContractTemplate template)
    {
        ulong hash = Hash($"{giver}:{day}:{template.Definition.LocalId}");
        double unit = (hash >> 11) * (1.0 / (1UL << 53));
        return Math.Pow(unit, 1.0 / template.Weight);
    }

    bool TryRegister(string giver, string local, ContractTemplate template)
    {
        try
        {
            context.Bounties.Register(template.Definition with { LocalId = local, Giver = giver });
            return true;
        }
        catch (Exception e)
        {
            context.Log.Warn($"could not create contract from '{template.Definition.LocalId}': {e.Message}");
            return false;
        }
    }

    string ContractId(string localId) => $"{context.Id}:bounty/{localId}";

    static ulong Hash(string text)
    {
        ulong hash = 14695981039346656037;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 1099511628211;
        }
        return hash;
    }

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Version);
        writer.Write(nextSerial);
        writer.Write(givers.Count);
        foreach (var (giver, state) in givers)
        {
            writer.Write(giver);
            writer.Write(state.TownId);
            writer.Write(state.TownName);
            writer.Write(state.Day);
            for (int slot = 0; slot < Slots; slot++)
            {
                writer.Write(state.Slots[slot]?.LocalId ?? "");
                writer.Write(state.Slots[slot]?.TemplateId ?? "");
            }
            writer.Write(state.Cooldowns.Count);
            foreach (var (templateId, until) in state.Cooldowns.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.Write(templateId);
                writer.Write(until);
            }
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        int version = reader.ReadInt32();
        if (version is not (1 or 2)) throw new InvalidDataException($"Unknown contract framework version {version}.");
        nextSerial = reader.ReadInt32();
        int count = reader.ReadInt32();
        if (count is < 0 or > 10_000) throw new InvalidDataException("Invalid contract giver count.");
        givers.Clear();
        for (int i = 0; i < count; i++)
        {
            string giver = reader.ReadString();
            var state = new GiverState
            {
                TownId = reader.ReadString(),
                TownName = reader.ReadString(),
                Day = reader.ReadInt32(),
                Slots = new SlotState?[Slots],
            };
            for (int slot = 0; slot < Slots; slot++)
            {
                string local = reader.ReadString();
                string templateId = reader.ReadString();
                if (local.Length == 0 || templateId.Length == 0) continue;
                if (owner.TemplateById(templateId) is not { } template)
                {
                    context.Log.Warn($"contract template '{templateId}' is no longer registered; dropping its saved slot");
                    continue;
                }
                if (TryRegister(giver, local, template)) state.Slots[slot] = new SlotState(local, templateId);
            }
            if (version >= 2)
            {
                int cooldownCount = reader.ReadInt32();
                if (cooldownCount is < 0 or > 10_000) throw new InvalidDataException("Invalid contract cooldown count.");
                for (int c = 0; c < cooldownCount; c++)
                {
                    string templateId = reader.ReadString();
                    int until = reader.ReadInt32();
                    if (templateId.Length > 0 && owner.TemplateById(templateId) is not null) state.Cooldowns[templateId] = until;
                }
            }
            givers[giver] = state;
        }
    }

    sealed class GiverState
    {
        public string TownId { get; set; } = "";
        public string TownName { get; set; } = "";
        public int Day { get; set; }
        public required SlotState?[] Slots { get; init; }
        public Dictionary<string, int> Cooldowns { get; init; } = new(StringComparer.Ordinal);
    }

    sealed record SlotState(string LocalId, string TemplateId);
}
