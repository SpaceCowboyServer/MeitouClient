using System.Security.Cryptography;
using System.Text.Json;
using Meitou.Modding;

namespace Meitou.Sdk;

public sealed record RepositoryEntry(string Id, string Version, string File, string Sha256);

public sealed record RepositoryIndex(int Format, IReadOnlyList<RepositoryEntry> Mods);

/// <summary>
/// A local distribution folder: flat <c>.meitoumod</c> files plus a generated <c>meitou-repository.json</c>. Workshop is deliberately outside this format.
/// </summary>
public static class ModRepository
{
    public const string IndexFileName = "meitou-repository.json";

    public static IReadOnlyList<RepositoryEntry> Scan(string folder)
    {
        string root = Path.GetFullPath(folder);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Repository '{root}' does not exist.");
        var entries = new List<RepositoryEntry>();
        foreach (string package in Directory.EnumerateFiles(root, "*.meitoumod", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal))
        {
            var info = ModPackage.ReadInfo(package);
            byte[] bytes = File.ReadAllBytes(package);
            entries.Add(new RepositoryEntry(info.Manifest.Id, info.Manifest.Version, Path.GetFileName(package),
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
        }
        var duplicates = entries.GroupBy(entry => (entry.Id, entry.Version)).Where(group => group.Count() > 1).ToList();
        if (duplicates.Count > 0)
            throw new InvalidDataException($"The repository has duplicate packages for {string.Join(", ", duplicates.Select(group => $"{group.Key.Id} {group.Key.Version}"))}.");
        return [.. entries.OrderBy(entry => entry.Id, StringComparer.Ordinal)
            .ThenBy(entry => entry.Version, StringComparer.Ordinal)];
    }

    public static RepositoryIndex WriteIndex(string folder, string? indexPath = null)
    {
        string root = Path.GetFullPath(folder);
        var index = new RepositoryIndex(1, Scan(root));
        string path = Path.GetFullPath(indexPath ?? Path.Combine(root, IndexFileName));
        var json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(index, json) + "\n");
        return index;
    }

    /// <summary>Finds the exact version, or the newest version when <paramref name="version"/> is null; returns an absolute path.</summary>
    public static string Resolve(string folder, string id, string? version = null)
    {
        if (!ModId.IsValid(id)) throw new ArgumentException($"'{id}' is not a mod ID.", nameof(id));
        var matches = Scan(folder).Where(entry => entry.Id == id).ToList();
        if (version is not null) matches = matches.Where(entry => entry.Version == version).ToList();
        if (matches.Count == 0) throw new FileNotFoundException($"No package for '{id}'{(version is null ? "" : " " + version)} in '{folder}'.");
        var selected = version is null
            ? matches.OrderByDescending(entry => Parse(entry.Version)).First()
            : matches[0];
        return Path.Combine(Path.GetFullPath(folder), selected.File);
    }

    static SemanticVersion Parse(string version) =>
        SemanticVersion.TryParse(version, out var parsed) ? parsed : default;
}
