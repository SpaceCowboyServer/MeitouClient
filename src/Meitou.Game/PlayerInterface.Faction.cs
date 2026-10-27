using System.Globalization;
using System.Numerics;
using Meitou.Data.Ui;
using Meitou.Simulation.Bounties;
using Meitou.Ui;

namespace Meitou.Game;

/// <summary>
/// The Overview's faction tab (<c>FactionsScreen</c>, docs/game/ui-screens.md "Faction tab"): the player faction's name, a <c>Kenshi_FactionRelationsLine</c> per faction with
/// the relation's number and its bar, and the information panel's two headings.
/// </summary>
sealed partial class PlayerInterface
{
    const int FactionTab = 1;

    /// <summary>The factions the faction tab lists with the player's relation towards each (-100..100); null before a world with game data. The host sets it.</summary>
    public Func<IReadOnlyList<(string Name, float Relation)>>? FactionRelations { get; set; }

    /// <summary>The last published bounty board (offers and accepted contracts); null before a world with the bounty system. The host sets it.</summary>
    public Func<BountyBoardView?>? Bounties { get; set; }

    MyGuiLayout? relationsLineLayout;
    float relationsScroll;

    bool FactionShown => faithfulScreens.IsOpen("overview") && faithfulScreens.GetTab("overview", "TabsMain") == FactionTab;

    const string FactionNameBox = "FactionNameText";

    /// <summary>What was typed in the name box and is not yet accepted (null: nothing).</summary>
    string? factionDraft;

    /// <summary>The giver contract whose journal detail is open (null: none).</summary>
    string? journalContract;

    /// <summary>
    /// The player faction's name in the tab's name box (the original's text is the player interface's faction name). The box takes typing; accepting it or losing the key focus renames
    /// (<see cref="UpdateFactionName"/>, <c>FUN_1404923d0</c>), and a box not being typed in shows the faction's current name.
    /// </summary>
    void BindFactionTab(UiBindings b)
    {
        string name = FactionName?.Invoke(session.World.Player.Faction) ?? "Nameless";
        if (faithfulScreens.Focus is not { Screen: "overview", Widget: FactionNameBox }) faithfulScreens.SetText("overview", FactionNameBox, name);
        b.Text[FactionNameBox] = name;
    }

    /// <summary>A text change in the name box is kept as the draft; the box losing the focus (or the tab closing) sends the rename of the last one. Bounty buttons go
    /// through the command queue like orders.</summary>
    void FactionAction(MyGuiEvent e)
    {
        if (e.Action.StartsWith("bounty-accept:", StringComparison.Ordinal))
        {
            Say(new AcceptBountyCommand(e.Action[14..]) { Tick = session.World.Tick });
            return;
        }
        if (e.Action.StartsWith("bounty-detail:", StringComparison.Ordinal))
        {
            journalContract = e.Action[14..];
            return;
        }
        if (e.Action == "bounty-detail-close")
        {
            journalContract = null;
            return;
        }
        if (e.Action.StartsWith("bounty-abandon:", StringComparison.Ordinal))
        {
            journalContract = null;
            Say(new AbandonBountyCommand(e.Action[15..]) { Tick = session.World.Tick });
            return;
        }
        if (e.Widget == FactionNameBox && e.Action == "text") factionDraft = e.Text;
    }

    /// <summary>Once a frame: a draft whose box no longer has the key focus is sent as <see cref="RenameFactionCommand"/>.</summary>
    void UpdateFactionName()
    {
        if (factionDraft is not { } draft || faithfulScreens.Focus is { Screen: "overview", Widget: FactionNameBox }) return;
        factionDraft = null;
        Say(new Meitou.Simulation.RenameFactionCommand(draft) { Tick = session.World.Tick });
    }

    /// <summary>
    /// The relations list (<c>FUN_140499960</c> / <c>FUN_140499630</c>): the lines sorted by faction name, each the panel's width less the scroll bar and stacked with 3 px (at
    /// 1080 lines) between them; the number in BadBright below -30, Good above 60, else Main; a negative relation fills the left bar leftwards from the middle, a positive one
    /// the right bar rightwards, |relation| % of their width.
    /// </summary>
    void AddFactionContent(List<UiControl> controls)
    {
        if (!FactionShown) return;
        var scale = Height / 1080f;
        if (controls.FirstOrDefault(c => c.Name == "FactionInfoPanel") is not null)
        {
            var board = Bounties?.Invoke();
            var journal = board?.Journal ?? [];
            BountyContractView? DetailOf(string id)
            {
                foreach (var contract in journal)
                    if (contract.Id == id) return contract;
                return null;
            }
            var detail = journalContract is { } selected ? DetailOf(selected) : null;
            if (journalContract is not null && detail is null) journalContract = null;
            var lines = new List<HudLine>();
            if (detail is { } detailContract)
            {
                // Details get their own page so the journal/offer sections cannot push the text off the panel.
                lines.Add(new(Loc.Tr("---Contract Details---"), "", GreyColour));
                lines.Add(new(Loc.Tr("Target:"), detailContract.Target));
                lines.Add(new(Loc.Tr("Giver:"), detailContract.GiverName.Length > 0 ? detailContract.GiverName : detailContract.Giver));
                lines.Add(new(Loc.Tr("Progress:"), $"{detailContract.Progress}/{detailContract.Required}"));
                lines.Add(new(Loc.Tr("Reward:"), Loc.Tr("c.{1,num}", detailContract.Reward)));
                lines.Add(new(Loc.Tr("Deadline:"), Deadline(detailContract.ExpiresAtGameHours, detailContract.State)));
                lines.Add(new(Loc.Tr("Status:"), ContractStatus(detailContract.State)));
                bool firstDetail = true;
                foreach (string part in WrapDetail(detailContract.Detail, 46))
                {
                    lines.Add(new(firstDetail ? Loc.Tr("Details:") : "", part));
                    firstDetail = false;
                }
                if (detailContract.State == BountyState.ReadyToTurnIn)
                    lines.Add(new(Loc.Tr("Turn in at:"), detailContract.GiverName.Length > 0 ? detailContract.GiverName : detailContract.Giver));
                lines.Add(new("", Loc.Tr("Abandon"), MainColour, BadBrightColour, Action: "bounty-abandon:" + detailContract.Id, ButtonWidth: 0.27f));
                lines.Add(new("", Loc.Tr("Close"), MainColour, MainColour, Action: "bounty-detail-close", ButtonWidth: 0.27f));
            }
            else
            {
                lines.Add(new(Loc.Tr("---Active Contracts---"), "", GreyColour));
                if (board is { Active.Count: > 0 })
                    foreach (var contract in board.Active)
                        lines.Add(new($"{contract.Title}  {contract.Progress}/{contract.Required}", Loc.Tr("Abandon"), MainColour, BadBrightColour,
                            TooltipRows: BountyTip(contract.Reward, contract.Distance), Action: "bounty-abandon:" + contract.Id, ButtonWidth: 0.27f));
                else
                    lines.Add(new(Loc.Tr("No active contracts"), "", GreyColour));
                if (journal.Count > 0)
                {
                    lines.Add(new("", ""));
                    lines.Add(new(Loc.Tr("---Giver Contracts---"), "", GreyColour));
                    foreach (var contract in journal)
                    {
                        string giver = contract.GiverName.Length > 0 ? contract.GiverName : contract.Giver;
                        string progress = contract.State == BountyState.ReadyToTurnIn ? Loc.Tr("Ready to turn in") : $"{contract.Progress}/{contract.Required}";
                        lines.Add(new($"{contract.Title}  {progress}", Loc.Tr("Details"), MainColour, GoodColour,
                            TooltipRows: JournalTip(contract, giver), Action: "bounty-detail:" + contract.Id, ButtonWidth: 0.27f));
                    }
                }
                lines.Add(new("", ""));
                lines.Add(new(Loc.Tr("---Available Contracts---"), "", GreyColour));
                if (board is { Offers.Count: > 0 })
                    foreach (var offer in board.Offers)
                        lines.Add(new($"{offer.Title}  {offer.Detail}", Loc.Tr("Accept"), MainColour, GoodColour,
                            TooltipRows: BountyTip(offer.Reward, offer.Distance), Action: "bounty-accept:" + offer.Id, ButtonWidth: 0.27f));
                else
                    lines.Add(new(Loc.Tr("No bounties nearby"), "", GreyColour));
                lines.Add(new("", ""));
                lines.Add(new(Loc.Tr("---Upcoming Events---"), "", GreyColour));
            }
            AddLines(controls, "FactionInfoPanel", lines, 20 * scale, StandardPixels);
        }
        if (relationsLineLayout is null || controls.FirstOrDefault(c => c.Name == "RelationsPanel") is not { } panel) return;
        var factions = (FactionRelations?.Invoke() ?? []).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var area = panel.Bounds;
        var clip = panel.Clip is { } outer ? area.Intersect(outer) : area;
        var root = relationsLineLayout.Roots.FirstOrDefault();
        float lineHeight = root is null ? 24 * scale : root.Position.RelativeTo(new(0, 0, Width, Height), root.RealPosition).Height;
        float lineWidth = area.Width - SquadScrollBar * scale, step = lineHeight + 3 * scale;
        relationsScroll = Math.Clamp(relationsScroll, 0, Math.Max(0, factions.Count * step - area.Height));
        for (int i = 0; i < factions.Count; i++)
        {
            var (name, relation) = factions[i];
            var line = new UiRect(area.X, area.Y + i * step - relationsScroll, lineWidth, lineHeight);
            if (line.Intersect(clip).Height <= 0) continue;
            var b = new UiBindings { TemplateScale = scale };
            if (root is not null) b.Positions[root.Name] = new(0, 0, lineWidth, lineHeight);
            b.Text["FactionName"] = name;
            b.Text["FactionValue"] = ((int)relation).ToString(CultureInfo.InvariantCulture);
            b.Properties["FactionValue"] = new Dictionary<string, string> { ["TextColour"] = relation < -30 ? BadBrightColour : relation > 60 ? GoodColour : MainColour };
            foreach (var (bar, negative) in new[] { ("LeftBar", true), ("RightBar", false) })
            {
                if (relationsLineLayout.Find(bar) is not { } widget) continue;
                var full = widget.Position.RelativeTo(new(0, 0, lineWidth, lineHeight), widget.RealPosition);
                float share = negative ? Math.Max(0, -relation) / 100 : Math.Max(0, relation) / 100;
                float width = full.Width * Math.Min(share, 1);
                b.Positions[bar] = negative ? full with { X = full.X + full.Width - width, Width = width } : full with { Width = width };
            }
            foreach (var c in MyGuiPresentation.Arrange(relationsLineLayout, (int)MathF.Round(lineWidth), (int)MathF.Round(lineHeight), b, uiResources.Layers))
            {
                var own = c.Clip is { } inner ? (inner with { X = inner.X + line.X, Y = inner.Y + line.Y }).Intersect(clip) : clip;
                controls.Add(c with { Name = c.Name.Length > 0 ? $"faction-{i}-{c.Name}" : c.Name, Bounds = c.Bounds with { X = c.Bounds.X + line.X, Y = c.Bounds.Y + line.Y }, Clip = own,
                    Properties = new Dictionary<string, string>(c.Properties, StringComparer.Ordinal) { ["FactionLine"] = "1" } });
            }
        }
    }

    static IEnumerable<string> WrapDetail(string text, int width)
    {
        if (text.Length == 0) yield break;
        string remaining = text;
        while (remaining.Length > width)
        {
            int cut = remaining.LastIndexOf(' ', width);
            if (cut <= 0) cut = width;
            yield return remaining[..cut];
            remaining = remaining[cut..].TrimStart();
        }
        yield return remaining;
    }

    string Deadline(double at, BountyState state) =>
        state == BountyState.ReadyToTurnIn ? Loc.Tr("Met")
        : at < 0 ? Loc.Tr("None") : at <= session.World.GameHours ? Loc.Tr("Due") : Loc.Tr("{1,num} h", (int)Math.Ceiling(at - session.World.GameHours));

    static string ContractStatus(BountyState state) => state switch
    {
        BountyState.ReadyToTurnIn => Loc.Tr("Ready to turn in"),
        BountyState.Completed => Loc.Tr("Completed"),
        BountyState.Failed => Loc.Tr("Failed"),
        BountyState.Expired => Loc.Tr("Expired"),
        _ => Loc.Tr("Active"),
    };

    IReadOnlyList<(string Key, string Value)> JournalTip(BountyContractView contract, string giver) =>
    [
        (Loc.Tr("Reward:"), Loc.Tr("c.{1,num}", contract.Reward)),
        (Loc.Tr("Giver:"), giver),
        (Loc.Tr("Progress:"), $"{contract.Progress}/{contract.Required}"),
        (Loc.Tr("Deadline:"), Deadline(contract.ExpiresAtGameHours, contract.State)),
        (Loc.Tr("Nearest target:"), float.IsFinite(contract.Distance) ? Loc.Tr("{1,num} m", (int)contract.Distance) : Loc.Tr("Unknown")),
    ];

    /// <summary>A bounty row's tooltip: the reward and the distance to the nearest living member of the target faction.</summary>
    static IReadOnlyList<(string Key, string Value)> BountyTip(int reward, float distance) =>
    [
        (Loc.Tr("Reward:"), Loc.Tr("c.{1,num}", reward)),
        (Loc.Tr("Nearest target:"), float.IsFinite(distance) ? Loc.Tr("{1,num} m", (int)distance) : Loc.Tr("Unknown")),
    ];

    /// <summary>The wheel over the relations list scrolls it.</summary>
    bool FactionWheel(Vector2 p, float steps)
    {
        if (!FactionShown || faithfulScreens.HitTest(p) is not { Screen: "overview" } hit || !(hit.Control.Name == "RelationsPanel" || hit.Control.Properties.ContainsKey("FactionLine"))) return false;
        relationsScroll -= steps * 44 * (Height / 1080f);   // clamped when the list is laid out
        return true;
    }
}
