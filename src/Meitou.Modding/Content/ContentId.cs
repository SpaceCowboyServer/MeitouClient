namespace Meitou.Modding.Content;

/// <summary>A content definition's stable ID: <c>modid:type/name</c>, for example <c>frontier.revolver:item/revolver</c>.</summary>
public readonly record struct ContentId(string Mod, string Type, string Name)
{
    public static bool TryParse(string? text, out ContentId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        int colon = text.IndexOf(':');
        if (colon <= 0 || colon == text.Length - 1) return false;
        return TryLocal(text[..colon], text[(colon + 1)..], out id);
    }

    /// <summary>Builds an ID from a mod and a local <c>type/name</c> pair.</summary>
    public static bool TryLocal(string mod, string? local, out ContentId id)
    {
        id = default;
        if (!Meitou.Modding.ModId.IsValid(mod) || string.IsNullOrWhiteSpace(local)) return false;
        int slash = local.IndexOf('/');
        if (slash <= 0 || slash == local.Length - 1 || local.IndexOf('/', slash + 1) >= 0) return false;
        return TryLocal(mod, local[..slash], local[(slash + 1)..], out id);
    }

    public static bool TryLocal(string mod, string? type, string? name, out ContentId id)
    {
        id = default;
        if (!Meitou.Modding.ModId.IsValid(mod) || !IsType(type) || !IsName(name)) return false;
        id = new ContentId(mod, type!, name!);
        return true;
    }

    public static bool IsType(string? type)
    {
        if (string.IsNullOrEmpty(type) || !char.IsAsciiLetter(type[0])) return false;
        foreach (char c in type)
            if ((!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-')) || char.IsAsciiLetterUpper(c))
                return false;
        return true;
    }

    public static bool IsName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (char c in name)
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.'))
                return false;
        return true;
    }

    public override string ToString() => $"{Mod}:{Type}/{Name}";
}
