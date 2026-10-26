using Meitou.Content;
using Meitou.Modding.Assets;

namespace Meitou.Modding.Vfs;

/// <summary>
/// The enabled mods' declared <c>assets</c> and <c>content</c> roots, mounted in load order. Asset IDs are stable and scoped to their
/// mod; a relative path lookup lets a later mod override an earlier one (the original game's art override rule).
/// </summary>
public sealed class ModVfs
{
    readonly Dictionary<AssetId, ModAssetFile> assetsById = [];
    readonly Dictionary<string, ModAssetFile> assetsByPath = new(StringComparer.Ordinal);
    readonly List<ModContentFile> contentFiles = [];
    readonly HashSet<string> contentPaths = new(StringComparer.Ordinal);
    readonly HashSet<string> mods = new(StringComparer.Ordinal);
    readonly List<ModIssue> issues = [];

    public IReadOnlyDictionary<AssetId, ModAssetFile> Assets => assetsById;
    public IReadOnlyList<ModContentFile> ContentFiles => contentFiles;
    public IReadOnlyList<ModIssue> Issues => issues;

    public static ModVfs Mount(IReadOnlyList<ModCandidate> mods)
    {
        var vfs = new ModVfs();
        foreach (var mod in mods) vfs.MountMod(mod);
        return vfs;
    }

    /// <summary>The asset with this ID, or null when its mod is not enabled or the declared root does not contain the file.</summary>
    public ModAssetFile? Find(AssetId id) => assetsById.GetValueOrDefault(id);

    /// <summary>The winning file for a path relative to an asset root; later mods override earlier ones.</summary>
    public ModAssetFile? Find(string relativePath)
    {
        if (!ModPath.IsSafeRelative(relativePath)) return null;
        return assetsByPath.GetValueOrDefault(relativePath.Replace('\\', '/'));
    }

    public IEnumerable<ModContentFile> ContentOf(string mod) => contentFiles.Where(file => file.Mod == mod);

    void MountMod(ModCandidate mod)
    {
        if (!mods.Add(mod.Id))
        {
            issues.Add(Error("vfs.mod_duplicate", $"Mod '{mod.Id}' is mounted more than once.", mod));
            return;
        }

        foreach (string root in mod.Manifest.Assets ?? [])
            MountRoot(mod, root, content: false);
        foreach (string root in mod.Manifest.Content ?? [])
            MountRoot(mod, root, content: true);
    }

    void MountRoot(ModCandidate mod, string root, bool content)
    {
        if (!ModPath.IsSafeRelative(root))
        {
            issues.Add(Error("vfs.root_path", $"'{mod.Id}' declares an unsafe {(content ? "content" : "asset")} root '{root}'.", mod, root));
            return;
        }

        string declared = Path.Combine(mod.Folder, root.Replace('\\', '/'));
        string? directory = GameFiles.ExistingDirectory(declared);
        if (directory is null)
        {
            issues.Add(Error("vfs.root_missing", $"'{mod.Id}' {(content ? "content" : "asset")} root '{root}' does not exist.", mod, root));
            return;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            issues.Add(Error("vfs.root_unreadable", $"'{mod.Id}' root '{root}' could not be read: {e.Message}", mod, root));
            return;
        }

        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (!ModPath.IsSafeRelative(relative))
            {
                issues.Add(Error("vfs.file_path", $"'{mod.Id}' contains a file outside its root: '{relative}'.", mod, relative));
                continue;
            }

            long length;
            try { length = new FileInfo(file).Length; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                issues.Add(Error("vfs.file_unreadable", $"'{mod.Id}' cannot read '{relative}': {e.Message}", mod, relative));
                continue;
            }

            if (content)
            {
                if (!IsYaml(relative)) continue;
                if (!contentPaths.Add(mod.Id + ":" + relative))
                {
                    issues.Add(Warning("vfs.content_duplicate", $"'{mod.Id}' mounts '{relative}' more than once.", mod, relative));
                    continue;
                }
                contentFiles.Add(new ModContentFile(mod.Id, mod.FolderName, relative, Path.GetFullPath(file)));
                continue;
            }

            var asset = new ModAssetFile(new AssetId(mod.Id, relative), Path.GetFullPath(file), length, AssetKinds.FromPath(relative));
            AddAsset(asset, mod, relative);
        }
    }

    void AddAsset(ModAssetFile asset, ModCandidate mod, string relative)
    {
        if (assetsById.TryGetValue(asset.Id, out var duplicate))
        {
            issues.Add(Warning("vfs.asset_duplicate",
                $"'{mod.Id}' mounts asset '{relative}' more than once; '{duplicate.FullPath}' is replaced by '{asset.FullPath}'.", mod, relative));
        }
        assetsById[asset.Id] = asset;

        if (assetsByPath.TryGetValue(relative, out var previous) && previous.Id.Mod != asset.Id.Mod)
        {
            issues.Add(Warning("vfs.asset_conflict",
                $"Asset path '{relative}' is provided by '{previous.Mod}' and '{asset.Mod}'; the later mod wins.", mod, relative));
        }
        assetsByPath[relative] = asset;
    }

    static bool IsYaml(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".yml", StringComparison.OrdinalIgnoreCase);
    }

    static ModIssue Error(string code, string message, ModCandidate mod, string? file = null) =>
        new(ModIssueSeverity.Error, code, message, mod.Id, mod.FolderName, file);

    static ModIssue Warning(string code, string message, ModCandidate mod, string? file = null) =>
        new(ModIssueSeverity.Warning, code, message, mod.Id, mod.FolderName, file);
}
