using Meitou.Data;
using Meitou.Simulation;

namespace Meitou.Modding.Api;

/// <summary>The entry type named by a <c>trust: plugin</c> manifest's <c>entry</c> field.</summary>
public interface IMod
{
    void Initialize(IModContext context);
}

/// <summary>The stable surface a C# mod receives after the data load and merge, before the first world is built.</summary>
public interface IModContext
{
    /// <summary>The manifest's stable ID.</summary>
    string Id { get; }
    string Version { get; }
    string Folder { get; }
    /// <summary>The modding contract version this game build implements.</summary>
    int ApiVersion { get; }
    /// <summary>The merged game database, the same instance the next world is built from.</summary>
    GameDatabase Data { get; }
    /// <summary>A stable salt for the mod's random streams; mix it into <see cref="Rng.Hash"/> keys.</summary>
    ulong RandomSalt { get; }
    IModLog Log { get; }
    IModServices Services { get; }

    /// <summary>
    /// Registers a per-world system factory. Systems are appended after the standard systems, in mod load order and registration order; the
    /// factory runs once for each new world, so systems must not be shared between worlds.
    /// </summary>
    void AddSystem(Func<ITickSystem> factory);

    /// <summary>Registers state written to this mod's sidecar in every save and read back before the next world is built.</summary>
    void AddSaveable(ISaveable saveable);

    /// <summary>
    /// Queues a command for the simulation. Before the first world exists it is held and queued as soon as a world is attached; afterwards it
    /// goes straight to <see cref="World.Commands"/>.
    /// </summary>
    void Enqueue(SimCommand command);
}

public interface IModLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

/// <summary>
/// A small, string-keyed registry shared by the loaded plugins. Earlier mods register first; a duplicate key is an error. Typed contracts
/// work when both sides reference the same contract assembly (the usual case for a framework and its content packs).
/// </summary>
public interface IModServices
{
    void Provide(string key, object service);
    bool TryGet(string key, out object? service);

    T? Get<T>(string key) where T : class => TryGet(key, out var service) ? service as T : null;
}

/// <summary>
/// One piece of a mod's save state. The key is stable across versions and unique within the mod; the host writes all saveables of a mod into
/// one <c>meitou-mods/&lt;id&gt;.bin</c> sidecar in the save folder, which the original game ignores.
/// </summary>
public interface ISaveable
{
    string Key { get; }
    void Write(Stream stream);
    void Read(Stream stream);
}
