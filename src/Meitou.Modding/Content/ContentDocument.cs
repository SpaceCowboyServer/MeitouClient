namespace Meitou.Modding.Content;

/// <summary>
/// One YAML content definition. <see cref="Id"/> and <see cref="Type"/> are the local parts; the registry prefixes the mod ID, so
/// <c>id: revolver</c> + <c>type: item</c> in <c>frontier.revolver</c> becomes <c>frontier.revolver:item/revolver</c>.
/// </summary>
public sealed class ContentDocument
{
    public const int CurrentSchema = 1;

    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public int Schema { get; set; } = CurrentSchema;
    public string? Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Role -> asset ID, e.g. <c>icon: frontier.revolver:weapons/revolver.png</c>.</summary>
    public Dictionary<string, string>? Assets { get; set; }

    /// <summary>Type-specific values; typed schemas read these later.</summary>
    public Dictionary<string, object?>? Data { get; set; }
}
