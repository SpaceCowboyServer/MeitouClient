using Meitou.Content;

namespace Meitou.Modding;

/// <summary>
/// Static checks for code mods that the content bundle cannot see: the plugin DLLs and Lua entry files must exist inside the mod folder. Syntax and
/// entry-type execution remain the runtime host's job, so a diagnostic here never loads or runs mod code.
/// </summary>
public static class ModFileDiagnostics
{
    const int MaxScriptBytes = 1 << 20;

    public static IReadOnlyList<ModIssue> Validate(ModResolution resolution)
    {
        var issues = new List<ModIssue>();
        foreach (var candidate in resolution.Order)
        {
            if (candidate.Manifest.Trust == ModTrust.Script)
                CheckScript(candidate, issues);
            else if (candidate.Manifest.Trust == ModTrust.Plugin)
                CheckPlugin(candidate, issues);
        }
        return issues;
    }

    static void CheckScript(ModCandidate candidate, List<ModIssue> issues)
    {
        string? entry = candidate.Manifest.Entry;
        if (string.IsNullOrWhiteSpace(entry) || !ModPath.IsSafeRelative(entry))
        {
            issues.Add(Error("mod.script_entry", $"'{candidate.Id}' has no safe script entry.", candidate, entry));
            return;
        }
        string path = GameFiles.Resolve(Path.Combine(candidate.Folder, entry.Replace('\\', '/')));
        if (!File.Exists(path))
        {
            issues.Add(Error("mod.script_missing", $"Script '{entry}' is not in the mod folder.", candidate, entry));
            return;
        }
        try
        {
            if (new FileInfo(path).Length > MaxScriptBytes)
                issues.Add(Error("mod.script_too_large", $"Script '{entry}' is larger than {MaxScriptBytes} bytes.", candidate, entry));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            issues.Add(Error("mod.script_unreadable", $"Script '{entry}' could not be read: {e.Message}", candidate, entry));
        }
    }

    static void CheckPlugin(ModCandidate candidate, List<ModIssue> issues)
    {
        if (candidate.Manifest.Assemblies.Count == 0)
            issues.Add(Error("mod.assemblies", $"'{candidate.Id}' has no assemblies.", candidate, null));
        foreach (string assembly in candidate.Manifest.Assemblies)
        {
            if (!ModPath.IsSafeRelative(assembly))
            {
                issues.Add(Error("mod.assembly_path", $"Assembly path '{assembly}' is not safe.", candidate, assembly));
                continue;
            }
            string path = GameFiles.Resolve(Path.Combine(candidate.Folder, assembly.Replace('\\', '/')));
            if (!File.Exists(path))
                issues.Add(Error("mod.assembly_missing", $"Assembly '{assembly}' is not in the mod folder.", candidate, assembly));
        }
    }

    static ModIssue Error(string code, string message, ModCandidate candidate, string? file) =>
        new(ModIssueSeverity.Error, code, message, candidate.Id, candidate.FolderName, file);
}
