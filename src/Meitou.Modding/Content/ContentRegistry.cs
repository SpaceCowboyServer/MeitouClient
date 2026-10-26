using Meitou.Modding.Assets;
using Meitou.Modding.Vfs;

namespace Meitou.Modding.Content;

public sealed record ContentEntry(ContentId Id, ContentDocument Document, ModContentFile File);

/// <summary>The validated fields an item's consumers need; <c>Data</c> stays available for later typed fields.</summary>
public sealed record ItemContent(
    ContentId Id,
    string Name,
    string? Description,
    AssetId Icon,
    AssetId Mesh,
    AssetId? Sound,
    IReadOnlyDictionary<string, object?> Data);

/// <summary>Every valid-looking definition found in the enabled content roots, keyed by its stable ID.</summary>
public sealed class ContentRegistry
{
    readonly Dictionary<ContentId, ContentEntry> entries;

    public ContentRegistry(Dictionary<ContentId, ContentEntry> entries) => this.entries = entries;

    public IReadOnlyDictionary<ContentId, ContentEntry> Entries => entries;
    public ContentEntry? Find(ContentId id) => entries.GetValueOrDefault(id);
    public IEnumerable<ContentEntry> OfType(string type) => entries.Values.Where(entry => entry.Id.Type == type);
    public IEnumerable<ContentEntry> Items => OfType("item");

    /// <summary>The item's typed fields, or null when it is absent or its required asset roles are not valid IDs.</summary>
    public ItemContent? FindItem(ContentId id)
    {
        if (Find(id) is not { } entry || entry.Id.Type != "item") return null;
        var document = entry.Document;
        if (document.Assets is null ||
            !document.Assets.TryGetValue("icon", out var iconText) || !AssetId.TryParse(iconText, out var icon) ||
            !document.Assets.TryGetValue("mesh", out var meshText) || !AssetId.TryParse(meshText, out var mesh))
            return null;

        AssetId? sound = null;
        if (document.Assets.TryGetValue("sound", out var soundText) && AssetId.TryParse(soundText, out var parsedSound))
            sound = parsedSound;
        return new ItemContent(id, document.Name ?? "", document.Description, icon, mesh, sound,
            document.Data ?? new Dictionary<string, object?>());
    }
}
