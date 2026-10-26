namespace Meitou.Modding;

/// <summary>A mod folder with a valid manifest. The folder name is the <c>.mod</c>/<c>mods.cfg</c> name, not the manifest ID.</summary>
public sealed record ModCandidate(string FolderName, string Folder, ModManifest Manifest, string ManifestSha256)
{
    public string Id => Manifest.Id;
}

public sealed class ModResolveOptions
{
    public int ApiVersion { get; init; } = ModApi.CurrentVersion;
    public SemanticVersion? GameVersion { get; init; }
}

public enum ModIssueSeverity { Warning, Error }

public sealed record ModIssue(
    ModIssueSeverity Severity,
    string Code,
    string Message,
    string? ModId = null,
    string? FolderName = null,
    string? File = null);

/// <summary>The enabled mods in load order, plus every problem found while resolving them.</summary>
public sealed record ModResolution(
    IReadOnlyList<ModCandidate> Order,
    IReadOnlyList<ModIssue> Issues,
    int ApiVersion,
    SemanticVersion? GameVersion)
{
    public bool Success => Issues.All(issue => issue.Severity != ModIssueSeverity.Error);
    public IReadOnlyList<ModIssue> Errors => [.. Issues.Where(issue => issue.Severity == ModIssueSeverity.Error)];
    public IReadOnlyList<ModIssue> Warnings => [.. Issues.Where(issue => issue.Severity == ModIssueSeverity.Warning)];
}

/// <summary>
/// Resolves the enabled mods against their manifests. Hard dependencies, API/game versions and conflicts are errors; optional
/// dependencies and soft <c>loadAfter</c> edges only apply when the named mod is enabled. The input order is the user's load order and
/// breaks ties, so one enabled mod list always resolves to the same order.
/// </summary>
public static class ModResolver
{
    sealed record Target(ModCandidate Provider, SemanticVersion Version, bool Enabled);

    public static ModResolution Resolve(
        IReadOnlyList<ModCandidate> candidates,
        IReadOnlyList<string> enabledIds,
        ModResolveOptions? options = null)
    {
        options ??= new ModResolveOptions();
        var issues = new List<ModIssue>();

        var allById = new Dictionary<string, ModCandidate>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            string? error = ModManifestValidation.Validate(candidate.Manifest);
            if (error is not null)
            {
                issues.Add(Error("manifest.invalid", $"'{candidate.FolderName}' has an invalid meitou.json: {error}", candidate.Manifest.Id));
                continue;
            }
            if (!allById.TryAdd(candidate.Id, candidate))
                issues.Add(Error("manifest.duplicate_id",
                    $"Mod ID '{candidate.Id}' is used by both '{allById[candidate.Id].FolderName}' and '{candidate.FolderName}'.",
                    candidate.Id));
        }

        var enabled = new List<ModCandidate>();
        var enabledSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in enabledIds)
        {
            if (!ModId.IsValid(id))
            {
                issues.Add(Error("enabled.invalid", $"Enabled mod '{id}' is not a mod ID.", id));
                continue;
            }
            if (!enabledSet.Add(id))
            {
                issues.Add(Warning("enabled.duplicate", $"Enabled mod '{id}' is listed more than once.", id));
                continue;
            }
            if (!allById.TryGetValue(id, out var candidate))
            {
                issues.Add(Error("enabled.missing", $"Enabled mod '{id}' is not installed or has no valid meitou.json.", id));
                continue;
            }
            enabled.Add(candidate);
        }

        var enabledById = enabled.ToDictionary(candidate => candidate.Id, StringComparer.Ordinal);
        var enabledIndex = enabled.Select((candidate, index) => (candidate.Id, index))
            .ToDictionary(pair => pair.Id, pair => pair.index, StringComparer.Ordinal);

        var capabilities = BuildCapabilities(allById.Values, enabledSet, issues);
        var prerequisites = enabled.ToDictionary(candidate => candidate.Id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

        foreach (var candidate in enabled)
        {
            if (candidate.Manifest.ApiVersion != options.ApiVersion)
                issues.Add(Error("api.mismatch",
                    $"'{candidate.Id}' needs modding API {candidate.Manifest.ApiVersion}; this build has {options.ApiVersion}.",
                    candidate.Id));

            if (options.GameVersion is { } game && !string.IsNullOrWhiteSpace(candidate.Manifest.GameVersion))
            {
                if (VersionRange.TryParse(candidate.Manifest.GameVersion, out var range) && !range.IsSatisfiedBy(game))
                    issues.Add(Error("game.mismatch",
                        $"'{candidate.Id}' supports game versions {range}; this build is {game}.", candidate.Id));
            }

            foreach (var (dependencyId, rangeText) in candidate.Manifest.Depends)
            {
                var target = FindTarget(dependencyId, enabledById, allById, capabilities);
                if (target is null)
                {
                    issues.Add(Error("dependency.missing",
                        $"'{candidate.Id}' requires '{dependencyId}', which is not installed.", candidate.Id));
                    continue;
                }
                if (!target.Enabled)
                {
                    issues.Add(Error("dependency.disabled",
                        $"'{candidate.Id}' requires '{dependencyId}', which is installed but disabled.", candidate.Id));
                    continue;
                }
                if (!CheckVersion(candidate, dependencyId, rangeText, target.Version, issues, optional: false)) continue;
                AddPrerequisite(candidate, target, prerequisites, issues, optional: false);
            }

            foreach (var (dependencyId, rangeText) in candidate.Manifest.Optional)
            {
                var target = FindTarget(dependencyId, enabledById, allById, capabilities);
                if (target is null) continue;
                if (!target.Enabled)
                {
                    issues.Add(Warning("optional.disabled",
                        $"'{candidate.Id}' optional dependency '{dependencyId}' is installed but disabled.", candidate.Id));
                    continue;
                }
                if (!CheckVersion(candidate, dependencyId, rangeText, target.Version, issues, optional: true)) continue;
                AddPrerequisite(candidate, target, prerequisites, issues, optional: true);
            }

            foreach (string dependencyId in candidate.Manifest.LoadAfter)
            {
                var target = FindTarget(dependencyId, enabledById, allById, capabilities);
                if (target is { Enabled: true })
                    AddPrerequisite(candidate, target, prerequisites, issues, optional: true);
            }

            foreach (string conflictId in candidate.Manifest.Conflicts)
            {
                var target = FindTarget(conflictId, enabledById, allById, capabilities);
                if (target is { Enabled: true })
                    issues.Add(Error("conflict", $"'{candidate.Id}' conflicts with '{conflictId}'.", candidate.Id));
            }
        }

        var order = TopologicalOrder(enabled, enabledIndex, prerequisites, issues);
        return new ModResolution(order, issues, options.ApiVersion, options.GameVersion);
    }

    static Dictionary<string, Target> BuildCapabilities(
        IEnumerable<ModCandidate> candidates,
        HashSet<string> enabled,
        List<ModIssue> issues)
    {
        var capabilities = new Dictionary<string, Target>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!SemanticVersion.TryParse(candidate.Manifest.Version, out var modVersion)) continue;
            foreach (string text in candidate.Manifest.Provides)
            {
                if (!ModCapability.TryParse(text, out var capability)) continue;
                var target = new Target(candidate, capability.Version ?? modVersion, enabled.Contains(candidate.Id));
                if (!capabilities.TryGetValue(capability.Id, out var existing))
                {
                    capabilities[capability.Id] = target;
                    continue;
                }

                if (target.Enabled && existing.Enabled && existing.Provider.Id != candidate.Id)
                    issues.Add(Error("provide.duplicate",
                        $"Capability '{capability.Id}' is provided by both '{existing.Provider.Id}' and '{candidate.Id}'.", candidate.Id));
                if (target.Enabled && !existing.Enabled)
                    capabilities[capability.Id] = target;
            }
        }
        return capabilities;
    }

    static Target? FindTarget(
        string id,
        Dictionary<string, ModCandidate> enabledById,
        Dictionary<string, ModCandidate> allById,
        Dictionary<string, Target> capabilities)
    {
        if (enabledById.TryGetValue(id, out var enabled))
            return new Target(enabled, ParseVersion(enabled), true);
        if (capabilities.TryGetValue(id, out var capability))
            return capability;
        if (allById.TryGetValue(id, out var disabled))
            return new Target(disabled, ParseVersion(disabled), false);
        return null;
    }

    static SemanticVersion ParseVersion(ModCandidate candidate) => SemanticVersion.Parse(candidate.Manifest.Version);

    static bool CheckVersion(
        ModCandidate dependent,
        string dependencyId,
        string rangeText,
        SemanticVersion found,
        List<ModIssue> issues,
        bool optional)
    {
        if (!VersionRange.TryParse(rangeText, out var range))
        {
            issues.Add(Error("dependency.range", $"'{dependent.Id}' has an invalid version range '{rangeText}' for '{dependencyId}'.", dependent.Id));
            return false;
        }
        if (range.IsSatisfiedBy(found)) return true;

        issues.Add(new ModIssue(
            optional ? ModIssueSeverity.Warning : ModIssueSeverity.Error,
            optional ? "optional.version" : "dependency.version",
            $"'{dependent.Id}' {((optional) ? "optionally requires" : "requires")} '{dependencyId}' {range}; found {found}.",
            dependent.Id));
        return false;
    }

    static void AddPrerequisite(
        ModCandidate dependent,
        Target target,
        Dictionary<string, HashSet<string>> prerequisites,
        List<ModIssue> issues,
        bool optional)
    {
        if (target.Provider.Id == dependent.Id)
        {
            issues.Add(new ModIssue(
                optional ? ModIssueSeverity.Warning : ModIssueSeverity.Error,
                optional ? "dependency.self_optional" : "dependency.self",
                $"'{dependent.Id}' depends on itself.", dependent.Id));
            return;
        }
        prerequisites[dependent.Id].Add(target.Provider.Id);
    }

    static List<ModCandidate> TopologicalOrder(
        List<ModCandidate> enabled,
        Dictionary<string, int> enabledIndex,
        Dictionary<string, HashSet<string>> prerequisites,
        List<ModIssue> issues)
    {
        var dependents = enabled.ToDictionary(candidate => candidate.Id, _ => new List<string>(), StringComparer.Ordinal);
        var incoming = enabled.ToDictionary(candidate => candidate.Id, candidate => prerequisites[candidate.Id].Count, StringComparer.Ordinal);
        foreach (var (dependent, required) in prerequisites)
            foreach (string provider in required)
                dependents[provider].Add(dependent);

        var ready = new SortedSet<string>(Comparer<string>.Create((left, right) =>
        {
            int compare = enabledIndex[left].CompareTo(enabledIndex[right]);
            return compare != 0 ? compare : string.CompareOrdinal(left, right);
        }));
        foreach (var candidate in enabled)
            if (incoming[candidate.Id] == 0) ready.Add(candidate.Id);

        var order = new List<ModCandidate>(enabled.Count);
        var placed = new HashSet<string>(StringComparer.Ordinal);
        while (ready.Count > 0)
        {
            string id = ready.Min!;
            ready.Remove(id);
            placed.Add(id);
            order.Add(enabled[enabledIndex[id]]);

            foreach (string dependent in dependents[id])
                if (--incoming[dependent] == 0) ready.Add(dependent);
        }

        if (placed.Count == enabled.Count) return order;

        var remaining = enabled.Where(candidate => !placed.Contains(candidate.Id)).Select(candidate => candidate.Id).ToList();
        var cycle = FindCycle(remaining, prerequisites);
        issues.Add(Error("dependency.cycle",
            cycle is { Count: > 0 } ? $"Load-order cycle: {string.Join(" -> ", cycle)}." : $"Load-order cycle among: {string.Join(", ", remaining)}."));

        // Keep every enabled mod in the result; Success is false, so callers must not load the order.
        foreach (string id in remaining.OrderBy(id => enabledIndex[id]))
            order.Add(enabled[enabledIndex[id]]);
        return order;
    }

    static List<string>? FindCycle(List<string> remaining, Dictionary<string, HashSet<string>> prerequisites)
    {
        var remainingSet = new HashSet<string>(remaining, StringComparer.Ordinal);
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new List<string>();

        List<string>? Visit(string id)
        {
            state[id] = 1;
            stack.Add(id);
            foreach (string dependency in prerequisites[id].Where(remainingSet.Contains).Order(StringComparer.Ordinal))
            {
                if (!state.TryGetValue(dependency, out int dependencyState))
                {
                    if (Visit(dependency) is { } cycle) return cycle;
                }
                else if (dependencyState == 1)
                {
                    int start = stack.IndexOf(dependency);
                    return [.. stack[start..], dependency];
                }
            }
            stack.RemoveAt(stack.Count - 1);
            state[id] = 2;
            return null;
        }

        foreach (string id in remaining)
        {
            if (state.ContainsKey(id)) continue;
            if (Visit(id) is { } cycle) return cycle;
        }
        return null;
    }

    static ModIssue Error(string code, string message, string? modId = null) =>
        new(ModIssueSeverity.Error, code, message, modId);

    static ModIssue Warning(string code, string message, string? modId = null) =>
        new(ModIssueSeverity.Warning, code, message, modId);
}
