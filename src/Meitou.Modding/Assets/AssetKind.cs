namespace Meitou.Modding.Assets;

/// <summary>What an asset is, inferred from its file extension and checked against the content schema's role.</summary>
public enum AssetKind { Other, Texture, Mesh, Audio, Font, Data }

public static class AssetKinds
{
    public static AssetKind FromPath(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".png" or ".dds" or ".tga" or ".jpg" or ".jpeg" or ".bmp" => AssetKind.Texture,
            ".mesh" => AssetKind.Mesh,
            ".wav" or ".ogg" or ".wem" or ".bank" or ".bnk" => AssetKind.Audio,
            ".ttf" or ".otf" => AssetKind.Font,
            ".yaml" or ".yml" or ".json" or ".txt" => AssetKind.Data,
            _ => AssetKind.Other,
        };
    }

    public static string Describe(AssetKind kind) => kind switch
    {
        AssetKind.Texture => "texture",
        AssetKind.Mesh => "mesh",
        AssetKind.Audio => "audio",
        AssetKind.Font => "font",
        AssetKind.Data => "data",
        _ => "file",
    };
}
