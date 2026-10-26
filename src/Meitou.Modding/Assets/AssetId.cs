namespace Meitou.Modding.Assets;

/// <summary>
/// A stable asset ID: <c>modid:relative/path</c>, where the path is relative to one of the mod's declared <c>assets</c> roots.
/// It is never an install or disk path, so content survives a different checkout or load order.
/// </summary>
public readonly record struct AssetId(string Mod, string Path)
{
    public static bool TryParse(string? text, out AssetId id)
    {
        id = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        int colon = text.IndexOf(':');
        if (colon <= 0 || colon == text.Length - 1) return false;

        string mod = text[..colon];
        string path = text[(colon + 1)..].Replace('\\', '/');
        if (!Meitou.Modding.ModId.IsValid(mod) || !Meitou.Modding.ModPath.IsSafeRelative(path) || path.EndsWith('/'))
            return false;

        id = new AssetId(mod, path);
        return true;
    }

    public override string ToString() => $"{Mod}:{Path}";
}
