using Meitou.Modding.Vfs;

namespace Meitou.Modding.Assets;

/// <summary>Imports one asset file into a typed runtime value. The stream is valid only for the call.</summary>
public interface IAssetImporter
{
    bool CanImport(ModAssetFile file);
    object Import(Stream stream, ModAssetFile file);
}

/// <summary>
/// Resolves asset IDs through the VFS and caches imported values with reference counting. Importers are chosen in registration order;
/// the game registers texture, mesh and audio importers, while tests can register a small one. Loading is synchronous; callers that
/// need it off the frame thread can run it on a worker.
/// </summary>
public sealed class AssetPipeline
{
    sealed class Entry(object value, int references)
    {
        public object Value { get; } = value;
        public int References { get; set; } = references;
    }

    readonly ModVfs vfs;
    readonly List<IAssetImporter> importers = [];
    readonly Dictionary<AssetId, Entry> cache = [];
    readonly object gate = new();

    public AssetPipeline(ModVfs vfs) => this.vfs = vfs;

    public void Register(IAssetImporter importer)
    {
        lock (gate) importers.Add(importer);
    }

    /// <summary>
    /// Resolves and imports <paramref name="id"/>, returning a handle that releases its reference when disposed. Acquiring the same
    /// value twice returns the cached instance; the last release disposes it.
    /// </summary>
    public AssetHandle<T> Acquire<T>(AssetId id) where T : class
    {
        ModAssetFile file;
        IAssetImporter importer;
        lock (gate)
        {
            file = vfs.Find(id) ?? throw new FileNotFoundException($"Asset '{id}' is not in an enabled mod's asset roots.");
            if (cache.TryGetValue(id, out var existing))
            {
                if (existing.Value is not T typed)
                    throw new InvalidOperationException($"Asset '{id}' is already loaded as {existing.Value.GetType().Name}, not {typeof(T).Name}.");
                existing.References++;
                return new AssetHandle<T>(this, id, typed);
            }

            importer = importers.FirstOrDefault(candidate => candidate.CanImport(file)) ??
                throw new NotSupportedException($"No asset importer accepts '{file.RelativePath}' ({file.Kind}).");
        }

        object value;
        using (var stream = File.OpenRead(file.FullPath))
            value = importer.Import(stream, file);
        if (value is not T requested)
        {
            (value as IDisposable)?.Dispose();
            throw new InvalidOperationException($"Importer for '{id}' returned {value?.GetType().Name ?? "null"}, not {typeof(T).Name}.");
        }

        lock (gate)
        {
            if (cache.TryGetValue(id, out var raced))
            {
                (requested as IDisposable)?.Dispose();
                if (raced.Value is not T racedTyped)
                    throw new InvalidOperationException($"Asset '{id}' is already loaded as {raced.Value.GetType().Name}, not {typeof(T).Name}.");
                raced.References++;
                return new AssetHandle<T>(this, id, racedTyped);
            }
            cache[id] = new Entry(requested, 1);
            return new AssetHandle<T>(this, id, requested);
        }
    }

    internal void Release(AssetId id)
    {
        lock (gate)
        {
            if (!cache.TryGetValue(id, out var entry)) return;
            if (--entry.References > 0) return;
            cache.Remove(id);
            (entry.Value as IDisposable)?.Dispose();
        }
    }
}

/// <summary>One reference to a cached asset. Disposing it returns the reference; disposing twice does nothing.</summary>
public sealed class AssetHandle<T>(AssetPipeline owner, AssetId id, T value) : IDisposable where T : class
{
    AssetPipeline? pipeline = owner;

    public AssetId Id { get; } = id;
    public T Value { get; } = value;

    public void Dispose()
    {
        var release = Interlocked.Exchange(ref pipeline, null);
        release?.Release(Id);
    }
}
