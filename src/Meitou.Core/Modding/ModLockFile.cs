using System.Text.Json;

namespace Meitou.Modding;

/// <summary>
/// The resolved load order written beside a profile: every enabled mod's ID, version, folder, trust and manifest hash. The game can
/// compare it with the manifests on disk to detect a changed setup; saves will record the same information.
/// </summary>
public sealed class ModLockFile
{
    public const string FileName = "profile.lock.json";
    public const int CurrentFormat = 1;

    public int Format { get; set; } = CurrentFormat;
    public int ApiVersion { get; set; } = ModApi.CurrentVersion;
    public string? GameVersion { get; set; }
    public List<ModLockEntry> Mods { get; set; } = [];

    public static ModLockFile FromResolution(ModResolution resolution) => new()
    {
        ApiVersion = resolution.ApiVersion,
        GameVersion = resolution.GameVersion?.ToString(),
        Mods =
        [
            .. resolution.Order.Select(candidate => new ModLockEntry
            {
                Id = candidate.Id,
                Version = SemanticVersion.Parse(candidate.Manifest.Version).ToString(),
                Folder = candidate.FolderName,
                Trust = candidate.Manifest.Trust,
                ManifestSha256 = candidate.ManifestSha256,
            }),
        ],
    };

    public string ToJson() => JsonSerializer.Serialize(this, ModdingJson.Default.ModLockFile);

    /// <summary>The lockfile, or null when it is missing, unreadable or of a newer format.</summary>
    public static ModLockFile? Read(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var lockFile = JsonSerializer.Deserialize(File.ReadAllBytes(path), ModdingJson.Default.ModLockFile);
            if (lockFile is null || lockFile.Format > CurrentFormat || lockFile.ApiVersion <= 0) return null;
            lockFile.Mods ??= [];
            foreach (var entry in lockFile.Mods)
            {
                if (entry is null || !ModId.IsValid(entry.Id) || !SemanticVersion.TryParse(entry.Version, out _) ||
                    string.IsNullOrWhiteSpace(entry.Folder))
                    return null;
            }
            return lockFile;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public void Write(string path) => File.WriteAllText(path, ToJson());
}

public sealed class ModLockEntry
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string Folder { get; set; } = "";
    public ModTrust Trust { get; set; }
    public string ManifestSha256 { get; set; } = "";
}
