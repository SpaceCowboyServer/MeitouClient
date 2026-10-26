using Meitou.Content;

namespace Meitou.Modding;

/// <summary>The valid manifests found on disk and the bad ones that could not become candidates.</summary>
public sealed record ModDiscoveryResult(IReadOnlyList<ModCandidate> Candidates, IReadOnlyList<ModIssue> Issues)
{
    public bool Success => Issues.All(issue => issue.Severity != ModIssueSeverity.Error);
}

/// <summary>Connects the existing Kenshi mod discovery (<see cref="ModList"/>) to the manifest/resolver layer.</summary>
public static class ModDiscovery
{
    public static ModDiscoveryResult Discover(IEnumerable<ModInfo> mods)
    {
        var candidates = new List<ModCandidate>();
        var issues = new List<ModIssue>();
        foreach (var mod in mods)
        {
            if (!mod.HasManifest) continue;
            if (mod.Manifest is { } manifest)
            {
                candidates.Add(new ModCandidate(mod.Name, mod.Folder, manifest, mod.ManifestSha256 ?? ""));
                continue;
            }
            issues.Add(new ModIssue(ModIssueSeverity.Error, "manifest.invalid",
                $"'{mod.Name}' has an invalid meitou.json: {mod.ManifestError ?? "the file could not be read"}.", FolderName: mod.Name));
        }
        return new ModDiscoveryResult(candidates, issues);
    }

    /// <summary>The manifest IDs of the mods enabled in <c>mods.cfg</c>, in that order. Legacy data-only mods have no ID here.</summary>
    public static IReadOnlyList<string> EnabledIds(GameInstall install, IReadOnlyList<ModInfo> mods) =>
        EnabledIds(ModList.ReadEnabled(install), mods);

    static IReadOnlyList<string> EnabledIds(IEnumerable<string> enabledNames, IReadOnlyList<ModInfo> mods)
    {
        var byName = mods.ToDictionary(mod => mod.Name, StringComparer.Ordinal);
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string name in enabledNames)
            if (byName.TryGetValue(name, out var mod) && mod.Manifest is { } manifest && seen.Add(manifest.Id))
                ids.Add(manifest.Id);
        return ids;
    }

    /// <summary>Discovers, then resolves, the install's enabled mods. Invalid disabled manifests are not errors for the load order.</summary>
    public static ModResolution Resolve(GameInstall install, IReadOnlyList<ModInfo>? mods = null, ModResolveOptions? options = null)
    {
        mods ??= ModList.Discover(install);
        var enabledNames = ModList.ReadEnabled(install);
        var enabledSet = enabledNames.ToHashSet(StringComparer.Ordinal);
        var discovery = Discover(mods);
        var discoveryIssues = discovery.Issues.Where(issue => issue.FolderName is null || enabledSet.Contains(issue.FolderName)).ToList();
        var resolution = ModResolver.Resolve(discovery.Candidates, EnabledIds(enabledNames, mods), options);
        return discoveryIssues.Count == 0
            ? resolution
            : resolution with { Issues = [.. discoveryIssues, .. resolution.Issues] };
    }
}
