using Meitou.Content;

namespace Meitou.Modding;

/// <summary>
/// One enabled mod as saved with a game: manifest ID and version for a manifest mod, or the folder name for a legacy data-only mod. The folder is kept
/// too, so a moved or renamed manifest can still be explained in the load report.
/// </summary>
public readonly record struct ModSnapshot(string Id, string Version, string Folder, ModTrust Trust)
{
    /// <summary>How a save matches a mod: by ID when it has one, else by its folder name.</summary>
    public string Key => Id.Length > 0 ? "id:" + Id : "folder:" + Folder;
    public string Name => Id.Length > 0 ? Id : Folder;
}

/// <summary>The enabled mods of a running install, in load order.</summary>
/// <summary>The differences between a save's mod set and the running one: problems (missing, version, trust) and informational notes (new mods).</summary>
public sealed record ModSetDiff(IReadOnlyList<string> Issues, IReadOnlyList<string> Notes);

public static class ModSnapshots
{
    /// <summary>
    /// Manifest mods in <paramref name="resolution"/> order, then enabled legacy data-only mods in <c>mods.cfg</c> order. This is what a save lists:
    /// code-only mods have no data file in the CAMERA <c>mods</c> list, and a version change can only be seen here.
    /// </summary>
    public static IReadOnlyList<ModSnapshot> Current(GameInstall install, ModResolution resolution)
    {
        var snapshots = new List<ModSnapshot>(resolution.Order.Count);
        var manifestFolders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in resolution.Order)
        {
            manifestFolders.Add(candidate.FolderName);
            snapshots.Add(new ModSnapshot(candidate.Id, candidate.Manifest.Version, candidate.FolderName, candidate.Manifest.Trust));
        }

        var available = ModList.FindMods(install);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in ModList.ReadEnabled(install))
            if (available.ContainsKey(name) && !manifestFolders.Contains(name) && seen.Add(name))
                snapshots.Add(new ModSnapshot("", "", name, ModTrust.Data));
        return snapshots;
    }

    /// <summary>
    /// Compares the mod set a save was made with to the one running now. A manifest mod is matched by ID (its folder may move); a legacy mod by folder.
    /// Missing manifest mods, version changes and trust changes are issues; newly enabled mods are notes (they can change the world but need not be an error).
    /// Legacy data files are already reported by the vanilla <c>mods</c> list and are not repeated as missing.
    /// </summary>
    public static ModSetDiff Compare(IReadOnlyList<ModSnapshot> saved, IReadOnlyList<ModSnapshot> current)
    {
        var issues = new List<string>();
        var notes = new List<string>();
        if (saved.Count == 0) return new ModSetDiff(issues, notes);
        var currentByKey = current.ToDictionary(mod => mod.Key, StringComparer.Ordinal);
        var savedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var old in saved)
        {
            savedKeys.Add(old.Key);
            if (!currentByKey.TryGetValue(old.Key, out var now))
            {
                if (old.Id.Length > 0)
                    issues.Add($"Missing mod '{old.Name}' ({Describe(old.Version, old.Trust)}), not loaded now.");
                continue;
            }
            if (now.Version != old.Version)
                issues.Add($"Mod '{old.Name}' is {now.Version} now; the save was made with {Describe(old.Version, old.Trust)}.");
            if (now.Trust != old.Trust)
                issues.Add($"Mod '{old.Name}' is now {now.Trust.ToString().ToLowerInvariant()}; the save was made with {old.Trust.ToString().ToLowerInvariant()}.");
        }
        foreach (var now in current)
            if (!savedKeys.Contains(now.Key))
            {
                string version = now.Version.Length > 0 ? " " + now.Version : "";
                notes.Add($"New since the save: {now.Name}{version} ({now.Trust.ToString().ToLowerInvariant()}).");
            }
        return new ModSetDiff(issues, notes);
    }

    static string Describe(string version, ModTrust trust) =>
        version.Length > 0 ? $"version {version}, {trust.ToString().ToLowerInvariant()}" : trust.ToString().ToLowerInvariant();
}
