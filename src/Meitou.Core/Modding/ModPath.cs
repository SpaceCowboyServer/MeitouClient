namespace Meitou.Modding;

/// <summary>Rules for paths declared by a manifest. They are relative to the mod folder and may not escape it.</summary>
public static class ModPath
{
    public static bool IsSafeRelative(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0) return false;

        string normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':')) return false;
        normalized = normalized.TrimEnd('/');
        if (normalized.Length == 0) return false;

        foreach (string segment in normalized.Split('/'))
            if (segment.Length == 0 || segment is "." or "..") return false;
        return true;
    }
}
