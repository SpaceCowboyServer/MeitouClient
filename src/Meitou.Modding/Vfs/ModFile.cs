using Meitou.Modding.Assets;

namespace Meitou.Modding.Vfs;

/// <summary>A file under one of a mod's declared asset roots, addressed by its stable <see cref="AssetId"/>.</summary>
public sealed record ModAssetFile(AssetId Id, string FullPath, long Length, AssetKind Kind)
{
    public string Mod => Id.Mod;
    public string RelativePath => Id.Path;
}

/// <summary>A YAML file under one of a mod's declared content roots.</summary>
public sealed record ModContentFile(string Mod, string FolderName, string RelativePath, string FullPath);
