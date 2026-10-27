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
        if (template.TownId is null || template.TownName is null)
            throw new ArgumentException("A contract template's town filters cannot be null.", nameof(template));
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
    const int Version = 1;

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
            return active.Ready
                ? $"Turn in: {active.Title} — {active.Detail}"
                : $"In progress: {active.Title} ({active.Progress}/{active.Required}) — {active.Detail}";
        if (context.Board.OffersFor(ctx.GiverId).FirstOrDefault(offer => offer.Id == id) is { } offer)
            return $"Take: {offer.Title} — {offer.Detail} ({offer.Required} required, c.{offer.Reward:N0}{(offer.DeadlineHours > 0 ? $", {offer.DeadlineHours}h" : "")})";
        if (context.Board.CompletedFor(ctx.GiverId).FirstOrDefault(contract => contract.Id == id) is { } completed)
            return $"{completed.Title} — completed; new work tomorrow";
        return null;
    }

    public void Select(DialogueContext ctx, int slot)
    {
        if (!ctx.IsBartender || !givers.TryGetValue(ctx.GiverId, out var state) || (uint)slot >= Slots ||
            state.Slots[slot] is not { } item) return;
        string id = ContractId(item.LocalId);
        if (context.Board.ActiveFor(ctx.GiverId).FirstOrDefault(contract => contract.Id == id) is { } active)
        {
            if (active.Ready) context.Board.TurnIn(id);
            return;
        }
        if (context.Board.OffersFor(ctx.GiverId).Any(offer => offer.Id == id)) context.Board.Accept(id, ctx.NpcId, ctx.NpcName);
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
            // Finished, abandoned or absent: replace this slot with a new contract for the next day.
            context.Bounties.Remove(id);
            state.Slots[slot] = null;
        }
        FillFreeSlots(giver, state, available);
    }

    void FillFreeSlots(string giver, GiverState state, IReadOnlyList<ContractTemplate> available)
    {
        if (available.Count == 0) return;
        var used = new HashSet<string>(state.Slots.Where(slot => slot is not null).Select(slot => slot!.TemplateId), StringComparer.Ordinal);
        var ordered = available
            .OrderBy(template => Hash($"{giver}:{state.Day}:{template.Definition.LocalId}"))
            .ToArray();
        for (int slot = 0; slot < Slots; slot++)
        {
            if (state.Slots[slot] is not null) continue;
            foreach (var template in ordered)
            {
                string templateId = template.Definition.LocalId;
                if (used.Contains(templateId)) continue;
                string local = $"contract-{nextSerial++}";
                if (!TryRegister(giver, local, template)) continue;
                state.Slots[slot] = new SlotState(local, templateId);
                used.Add(templateId);
                break;
            }
        }
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
        }
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        int version = reader.ReadInt32();
        if (version != Version) throw new InvalidDataException($"Unknown contract framework version {version}.");
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
            givers[giver] = state;
        }
    }

    sealed class GiverState
    {
        public string TownId { get; set; } = "";
        public string TownName { get; set; } = "";
        public int Day { get; set; }
        public required SlotState?[] Slots { get; init; }
    }

    sealed record SlotState(string LocalId, string TemplateId);
}
