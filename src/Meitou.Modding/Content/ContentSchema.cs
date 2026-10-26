using Meitou.Modding.Assets;
using Meitou.Modding.Vfs;

namespace Meitou.Modding.Content;

/// <summary>A type-specific content validator, for example an item's required assets.</summary>
public interface IContentSchema
{
    string Type { get; }
    void Validate(ContentValidationContext context);
}

/// <summary>The definition being validated and the services a schema needs. A schema reports problems through <see cref="Error"/>.</summary>
public sealed class ContentValidationContext
{
    readonly Dictionary<string, AssetId> assets;
    readonly List<ModIssue> issues;

    internal ContentValidationContext(
        ContentId id,
        ContentDocument document,
        ModContentFile file,
        ModVfs vfs,
        Dictionary<string, AssetId> assets,
        List<ModIssue> issues)
    {
        Id = id;
        Document = document;
        File = file;
        Vfs = vfs;
        this.assets = assets;
        this.issues = issues;
    }

    public ContentId Id { get; }
    public ContentDocument Document { get; }
    public ModContentFile File { get; }
    public ModVfs Vfs { get; }
    public IReadOnlyDictionary<string, AssetId> Assets => assets;

    public void Error(string code, string message) =>
        issues.Add(new ModIssue(ModIssueSeverity.Error, code, message, Id.Mod, File.FolderName, File.RelativePath));

    /// <summary>Looks up a role's asset and checks its kind. A missing required role is an error; a missing optional role is null.</summary>
    public AssetId? Asset(string role, AssetKind expected, bool required)
    {
        if (!assets.TryGetValue(role, out var id))
        {
            if (required) Error("content.asset_required", $"Content '{Id}' needs an asset in role '{role}'.");
            return null;
        }

        if (Vfs.Find(id) is { } file && file.Kind != expected)
            Error("content.asset_kind",
                $"Asset '{id}' in role '{role}' is {AssetKinds.Describe(file.Kind)}; expected {AssetKinds.Describe(expected)}.");
        return id;
    }
}

/// <summary>Content types a build understands. A plugin can add its own schema with a new registry.</summary>
public sealed class ContentSchemaRegistry
{
    readonly Dictionary<string, IContentSchema> schemas;

    public ContentSchemaRegistry(IEnumerable<IContentSchema> schemas)
    {
        this.schemas = new Dictionary<string, IContentSchema>(StringComparer.Ordinal);
        foreach (var schema in schemas)
            if (!this.schemas.TryAdd(schema.Type, schema))
                throw new ArgumentException($"Content type '{schema.Type}' is registered more than once.", nameof(schemas));
    }

    public IReadOnlyCollection<string> Types => schemas.Keys;

    public static ContentSchemaRegistry CreateDefault() => new(
    [
        new ItemContentSchema(),
        new NamedContentSchema("faction"),
        new NamedContentSchema("contract"),
        new GenericContentSchema("dialogue"),
        new GenericContentSchema("localization"),
    ]);

    public bool TryGet(string type, out IContentSchema schema) => schemas.TryGetValue(type, out schema!);
}

/// <summary>An item's display name and the assets its inventory/rendering path needs.</summary>
public sealed class ItemContentSchema : IContentSchema
{
    public string Type => "item";

    public void Validate(ContentValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Document.Name))
            context.Error("content.item_name", $"Item content '{context.Id}' needs a non-empty name.");
        context.Asset("icon", AssetKind.Texture, required: true);
        context.Asset("mesh", AssetKind.Mesh, required: true);
        context.Asset("sound", AssetKind.Audio, required: false);
    }
}

public sealed class NamedContentSchema : IContentSchema
{
    public NamedContentSchema(string type) => Type = type;

    public string Type { get; }

    public void Validate(ContentValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Document.Name))
            context.Error($"content.{Type}_name", $"Content '{context.Id}' needs a non-empty name.");
    }
}

public sealed class GenericContentSchema : IContentSchema
{
    public GenericContentSchema(string type) => Type = type;

    public string Type { get; }
    public void Validate(ContentValidationContext context) { }
}
