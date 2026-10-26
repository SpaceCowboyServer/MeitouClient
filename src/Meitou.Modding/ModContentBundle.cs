using Meitou.Modding.Assets;
using Meitou.Modding.Content;
using Meitou.Modding.Vfs;

namespace Meitou.Modding;

/// <summary>The enabled mods after manifest resolution, their mounted assets/content and every validation problem found so far.</summary>
public sealed class ModContentBundle
{
    ModContentBundle(
        ModResolution resolution,
        ModVfs vfs,
        ContentRegistry content,
        IReadOnlyList<ModIssue> issues)
    {
        Resolution = resolution;
        Vfs = vfs;
        Content = content;
        Issues = issues;
    }

    public ModResolution Resolution { get; }
    public ModVfs Vfs { get; }
    public ContentRegistry Content { get; }
    public IReadOnlyList<ModIssue> Issues { get; }

    public bool Success => Issues.All(issue => issue.Severity != ModIssueSeverity.Error);
    public IReadOnlyList<ModIssue> Errors => [.. Issues.Where(issue => issue.Severity == ModIssueSeverity.Error)];
    public IReadOnlyList<ModIssue> Warnings => [.. Issues.Where(issue => issue.Severity == ModIssueSeverity.Warning)];

    public static ModContentBundle Load(ModResolution resolution, ContentSchemaRegistry? schemas = null)
    {
        var vfs = ModVfs.Mount(resolution.Order);
        var content = ContentLoader.Load(vfs, schemas);
        return new ModContentBundle(
            resolution,
            vfs,
            content.Registry,
            [.. resolution.Issues, .. vfs.Issues, .. content.Issues]);
    }

    public AssetPipeline CreateAssetPipeline() => new(Vfs);
}
