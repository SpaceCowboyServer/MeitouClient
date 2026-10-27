using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meitou.Modding;

/// <summary>
/// The <c>meitou.json</c> manifest beside a mod's <c>.mod</c> file. It declares the stable ID, version, dependencies, content/assets and
/// the code tier. Legacy Kenshi mods without one still load as data-only mods; Meitou-specific code, content and assets need it.
/// </summary>
public sealed class ModManifest
{
    public const string FileName = "meitou.json";

    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public int ApiVersion { get; set; } = ModApi.CurrentVersion;
    public string? GameVersion { get; set; }
    public Dictionary<string, string> Depends { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Optional { get; set; } = new(StringComparer.Ordinal);
    public List<string> Provides { get; set; } = [];
    public List<string> LoadAfter { get; set; } = [];
    public List<string> Conflicts { get; set; } = [];
    public List<string> Permissions { get; set; } = [];
    public List<string> Content { get; set; } = [];
    public List<string> Assets { get; set; } = [];
    public ModTrust Trust { get; set; } = ModTrust.Data;
    public string? Entry { get; set; }
    public List<string> Assemblies { get; set; } = [];

    /// <summary>
    /// Reads and validates a manifest. A missing file is not an error (legacy data mods have none); a malformed or invalid one returns
    /// its error and no manifest. The hash covers the file's bytes, so a lockfile can notice a changed manifest.
    /// </summary>
    public static ModManifestReadResult Read(string path)
    {
        if (!File.Exists(path)) return new(null, null, null);

        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(null, e.Message, null);
        }

        string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return Parse(bytes, hash);
    }

    /// <summary>The same validation as <see cref="Read"/>, for manifests already in memory (a mod package or tool).</summary>
    public static ModManifestReadResult Parse(byte[] bytes, string? manifestSha256 = null)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize(bytes, ModdingJson.Default.ModManifest);
            if (manifest is null) return new(null, "The manifest is empty.", manifestSha256);
            string? error = ModManifestValidation.Validate(manifest);
            return error is null ? new(manifest, null, manifestSha256) : new(null, error, manifestSha256);
        }
        catch (JsonException e)
        {
            return new(null, e.Message, manifestSha256);
        }
    }

    /// <summary>Fills collections that JSON explicitly set to null, so validation and resolution can assume they exist.</summary>
    internal void Normalize()
    {
        Id ??= "";
        Version ??= "";
        Depends = Depends is null ? new(StringComparer.Ordinal) : new(Depends, StringComparer.Ordinal);
        Optional = Optional is null ? new(StringComparer.Ordinal) : new(Optional, StringComparer.Ordinal);
        Provides ??= [];
        LoadAfter ??= [];
        Conflicts ??= [];
        Permissions ??= [];
        Content ??= [];
        Assets ??= [];
        Assemblies ??= [];
    }
}

/// <summary>The outcome of <see cref="ModManifest.Read"/>: a manifest, an error, or neither when the file is absent.</summary>
public readonly record struct ModManifestReadResult(ModManifest? Manifest, string? Error, string? ManifestSha256)
{
    public bool Success => Manifest is not null;
}

/// <summary>Checks all manifest fields that do not need the rest of the load order.</summary>
public static class ModManifestValidation
{
    public static string? Validate(ModManifest manifest)
    {
        manifest.Normalize();

        if (!ModId.IsValid(manifest.Id))
            return "id must be a namespaced ID such as 'frontier.revolver'.";
        if (!SemanticVersion.TryParse(manifest.Version, out _))
            return "version must be a semantic version such as '1.0.0'.";
        if (manifest.ApiVersion <= 0)
            return "apiVersion must be positive.";
        if (!string.IsNullOrWhiteSpace(manifest.GameVersion) && !VersionRange.TryParse(manifest.GameVersion, out _))
            return $"gameVersion '{manifest.GameVersion}' is not a version range.";

        string? error = ValidateDependencies(manifest.Depends, "depends");
        if (error is not null) return error;
        error = ValidateDependencies(manifest.Optional, "optional");
        if (error is not null) return error;

        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? text in manifest.Provides)
        {
            if (!ModCapability.TryParse(text, out var capability))
                return $"provides entry '{text}' is not a capability such as 'frontier.contracts@1'.";
            if (!capabilities.Add(capability.Id))
                return $"provides lists '{capability.Id}' more than once.";
        }

        foreach (string? id in manifest.LoadAfter)
            if (!ModId.IsValid(id)) return $"loadAfter entry '{id}' is not a mod ID.";
        foreach (string? id in manifest.Conflicts)
            if (!ModId.IsValid(id)) return $"conflicts entry '{id}' is not a mod ID.";

        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (string? permission in manifest.Permissions)
        {
            if (!ModPermissions.IsKnown(permission))
                return $"permission '{permission}' is not known; use 'events:world', 'events:bounty' or 'contracts:write'.";
            if (!permissions.Add(permission)) return $"permissions lists '{permission}' more than once.";
        }

        foreach (string? path in manifest.Content)
            if (!ModPath.IsSafeRelative(path)) return $"content path '{path}' must stay inside the mod folder.";
        foreach (string? path in manifest.Assets)
            if (!ModPath.IsSafeRelative(path)) return $"asset path '{path}' must stay inside the mod folder.";

        foreach (string? assembly in manifest.Assemblies)
        {
            if (!ModPath.IsSafeRelative(assembly)) return $"assembly path '{assembly}' must stay inside the mod folder.";
            if (!assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return $"assembly '{assembly}' must be a .dll.";
        }

        if (manifest.Trust is (ModTrust.Data or ModTrust.Script) && manifest.Assemblies.Count > 0)
            return $"trust '{manifest.Trust.ToString().ToLowerInvariant()}' mods cannot ship assemblies.";
        if (manifest.Trust is ModTrust.Plugin && manifest.Assemblies.Count == 0)
            return "trust 'plugin' requires at least one assembly.";
        if (manifest.Trust is (ModTrust.Plugin or ModTrust.Script) && string.IsNullOrWhiteSpace(manifest.Entry))
            return $"trust '{manifest.Trust.ToString().ToLowerInvariant()}' requires an entry.";
        if (manifest.Trust is ModTrust.Script && !ModPath.IsSafeRelative(manifest.Entry))
            return "a script entry must be a path inside the mod folder.";

        return null;
    }

    static string? ValidateDependencies(Dictionary<string, string> dependencies, string field)
    {
        foreach (var (id, range) in dependencies)
        {
            if (!ModId.IsValid(id)) return $"{field} key '{id}' is not a mod ID.";
            if (string.IsNullOrWhiteSpace(range))
                return $"{field} entry '{id}' needs a version range (use \"*\" for any).";
            if (!VersionRange.TryParse(range, out _))
                return $"{field} entry '{id}' has an invalid version range '{range}'.";
        }
        return null;
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ModManifest))]
[JsonSerializable(typeof(ModLockFile))]
sealed partial class ModdingJson : JsonSerializerContext;
