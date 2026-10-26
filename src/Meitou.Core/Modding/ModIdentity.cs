namespace Meitou.Modding;

/// <summary>The modding contract version this build implements. A manifest with another <c>apiVersion</c> is refused.</summary>
public static class ModApi
{
    public const int CurrentVersion = 1;
}

/// <summary>Namespaced, case-sensitive IDs. A mod ID has at least two dot-separated segments, e.g. <c>frontier.revolver</c>.</summary>
public static class ModId
{
    public static bool IsValid(string? id)
    {
        if (string.IsNullOrEmpty(id)) return false;

        int segments = 0;
        int start = 0;
        for (int i = 0; i <= id.Length; i++)
        {
            if (i != id.Length && id[i] != '.') continue;
            if (i == start) return false;
            for (int j = start; j < i; j++)
            {
                char c = id[j];
                if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')) return false;
            }
            segments++;
            start = i + 1;
        }
        return segments >= 2;
    }
}

/// <summary>
/// A capability a mod provides to others, optionally with the capability's own version: <c>frontier.contracts@1</c>. A dependency on it
/// resolves to the provider and checks its version range against this capability version, or against the provider's mod version when omitted.
/// </summary>
public readonly record struct ModCapability(string Id, SemanticVersion? Version)
{
    public static bool TryParse(string? text, out ModCapability capability)
    {
        capability = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        int at = text.LastIndexOf('@');
        string id = at >= 0 ? text[..at] : text;
        if (!ModId.IsValid(id)) return false;

        SemanticVersion? version = null;
        if (at >= 0)
        {
            if (!SemanticVersion.TryParsePartial(text[(at + 1)..], out var parsed)) return false;
            version = parsed;
        }

        capability = new ModCapability(id, version);
        return true;
    }

    public override string ToString() => Version is { } version ? $"{Id}@{version}" : Id;
}
