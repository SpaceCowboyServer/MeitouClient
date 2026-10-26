using Meitou.Modding.Assets;
using Meitou.Modding.Vfs;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Meitou.Modding.Content;

public sealed record ContentLoadResult(ContentRegistry Registry, IReadOnlyList<ModIssue> Issues)
{
    public bool Success => Issues.All(issue => issue.Severity != ModIssueSeverity.Error);
}

/// <summary>
/// Reads every YAML file under the enabled mods' <c>content</c> roots, qualifies its ID with the mod, validates it against a schema
/// and checks every referenced asset through the <see cref="ModVfs"/>. A file with a problem contributes issues but does not stop
/// the other files from loading.
/// </summary>
public static class ContentLoader
{
    public const int MaxDocumentBytes = 1 << 20;

    public static ContentLoadResult Load(ModVfs vfs, ContentSchemaRegistry? schemas = null)
    {
        schemas ??= ContentSchemaRegistry.CreateDefault();
        var yaml = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .WithDuplicateKeyChecking()
            .IgnoreUnmatchedProperties()
            .Build();
        var entries = new Dictionary<ContentId, ContentEntry>();
        var issues = new List<ModIssue>();

        foreach (var file in vfs.ContentFiles)
        {
            long length;
            try { length = new FileInfo(file.FullPath).Length; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                issues.Add(Error("content.unreadable", $"Content file '{file.RelativePath}' could not be read: {e.Message}", file));
                continue;
            }
            if (length > MaxDocumentBytes)
            {
                issues.Add(Error("content.too_large", $"Content file '{file.RelativePath}' is larger than {MaxDocumentBytes} bytes.", file));
                continue;
            }

            string text;
            try { text = File.ReadAllText(file.FullPath); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                issues.Add(Error("content.unreadable", $"Content file '{file.RelativePath}' could not be read: {e.Message}", file));
                continue;
            }

            ContentDocument? document;
            try { document = yaml.Deserialize<ContentDocument>(text); }
            catch (YamlException e)
            {
                string line = e.Start.Line > 0 ? $" at line {e.Start.Line}" : "";
                issues.Add(Error("content.yaml", $"Content file '{file.RelativePath}' is not valid YAML{line}: {e.Message}", file));
                continue;
            }
            if (document is null)
            {
                issues.Add(Error("content.empty", $"Content file '{file.RelativePath}' is empty.", file));
                continue;
            }

            document.Id = document.Id?.Trim() ?? "";
            document.Type = document.Type?.Trim() ?? "";
            document.Assets ??= [];
            document.Data ??= [];

            if (!ContentId.TryLocal(file.Mod, document.Type, document.Id, out var id))
            {
                issues.Add(Error("content.id",
                    $"Content file '{file.RelativePath}' needs a local id and a lowercase type; got id '{document.Id}', type '{document.Type}'.", file));
                continue;
            }
            if (document.Schema is < 1 or > ContentDocument.CurrentSchema)
            {
                issues.Add(Error("content.schema",
                    $"Content '{id}' uses schema {document.Schema}; this build supports 1 through {ContentDocument.CurrentSchema}.", file, id.Mod));
                continue;
            }
            if (!schemas.TryGet(document.Type, out var schema))
            {
                issues.Add(Error("content.type",
                    $"Content '{id}' has unknown type '{document.Type}'; known types: {string.Join(", ", schemas.Types.Order(StringComparer.Ordinal))}.",
                    file, id.Mod));
                continue;
            }
            if (entries.ContainsKey(id))
            {
                issues.Add(Error("content.duplicate", $"Content ID '{id}' is defined by more than one file.", file, id.Mod));
                continue;
            }

            var assets = ParseAssets(document, vfs, file, id, issues);
            schema.Validate(new ContentValidationContext(id, document, file, vfs, assets, issues));
            entries.Add(id, new ContentEntry(id, document, file));
        }

        return new ContentLoadResult(new ContentRegistry(entries), issues);
    }

    static Dictionary<string, AssetId> ParseAssets(
        ContentDocument document,
        ModVfs vfs,
        ModContentFile file,
        ContentId id,
        List<ModIssue> issues)
    {
        var assets = new Dictionary<string, AssetId>(StringComparer.Ordinal);
        foreach (var (role, value) in document.Assets ?? [])
        {
            if (!IsRole(role))
            {
                issues.Add(Error("content.asset_role", $"Content '{id}' has invalid asset role '{role}'.", file, id.Mod));
                continue;
            }
            if (!AssetId.TryParse(value, out var assetId))
            {
                issues.Add(Error("content.asset_id", $"Content '{id}' role '{role}' has invalid asset ID '{value}'.", file, id.Mod));
                continue;
            }
            assets[role] = assetId;
            if (vfs.Find(assetId) is null)
                issues.Add(Error("content.asset_missing",
                    $"Content '{id}' role '{role}' references '{assetId}', which is not in an enabled mod's asset roots.", file, id.Mod));
        }
        return assets;
    }

    static bool IsRole(string role)
    {
        if (string.IsNullOrEmpty(role) || !char.IsAsciiLetterLower(role[0])) return false;
        foreach (char c in role)
            if (!char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c is not ('_' or '-'))
                return false;
        return true;
    }

    static ModIssue Error(string code, string message, ModContentFile file, string? modId = null) =>
        new(ModIssueSeverity.Error, code, message, modId ?? file.Mod, file.FolderName, file.RelativePath);
}
