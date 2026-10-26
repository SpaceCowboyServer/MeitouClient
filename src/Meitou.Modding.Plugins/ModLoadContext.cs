using System.Reflection;
using System.Runtime.Loader;

namespace Meitou.Modding.Plugins;

/// <summary>
/// One non-collectible load context per plugin mod. Engine and shared framework assemblies resolve from the default context; a mod's
/// other dependencies resolve from DLLs under its folder. No unload and no hot reload: registrations hold the context's types.
/// </summary>
sealed class ModLoadContext : AssemblyLoadContext
{
    readonly Dictionary<string, string> privateAssemblies;

    public ModLoadContext(string name, string folder) : base(name, isCollectible: false)
    {
        privateAssemblies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                privateAssemblies.TryAdd(Path.GetFileNameWithoutExtension(file), file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The manifest's own assemblies are still loaded explicitly; only extra dependencies may be missing.
        }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is null) return null;

        try { return Default.LoadFromAssemblyName(assemblyName); }
        catch (Exception e) when (e is FileNotFoundException or FileLoadException or BadImageFormatException) { }

        // Engine types must keep their identity from the game's default context, never a private copy.
        if (assemblyName.Name.StartsWith("Meitou.", StringComparison.Ordinal)) return null;

        return privateAssemblies.TryGetValue(assemblyName.Name, out string? path) ? LoadFromAssemblyPath(path) : null;
    }

    public Assembly LoadPlugin(string path) => LoadFromAssemblyPath(path);
}
