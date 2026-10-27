using System.Reflection;
using Meitou.Content;
using Meitou.Data;
using Meitou.Modding;
using Meitou.Modding.Api;
using Meitou.Simulation;

namespace Meitou.Modding.Plugins;

/// <summary>
/// Loads the enabled <c>trust: plugin</c> manifests after the data merge, one non-collectible load context per mod and in load order, and
/// owns the registrations they make: services, per-world systems, commands and save sidecars.
/// </summary>
public sealed class ModPluginHost
{
    readonly Action<string> write;
    readonly List<ModSession> sessions = [];
    readonly List<Func<ITickSystem>> systemFactories = [];
    readonly List<ISaveable> saveables = [];
    readonly ModServices services = new();
    readonly List<SimCommand> pendingCommands = [];
    readonly object gate = new();
    World? world;

    ModPluginHost(Action<string> write) => this.write = write;

    public int PluginCount => sessions.Count;
    public int SystemCount => systemFactories.Count;
    public int SaveableCount => saveables.Count;
    public IReadOnlyList<string> LoadedModIds => [.. sessions.Select(session => session.Id)];
    public IModServices Services => services;

    public static ModPluginHost Load(GameInstall install, GameDatabase data, IModApiHost api, Action<string>? write = null) =>
        Load(ModDiscovery.Resolve(install), data, api, write);

    public static ModPluginHost Load(ModResolution resolution, GameDatabase data, IModApiHost api, Action<string>? write = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        var log = write ?? Console.WriteLine;
        var host = new ModPluginHost(log);
        foreach (var issue in resolution.Errors)
            log($"mods: {issue.Code}: {issue.Message}");
        foreach (var candidate in resolution.Order)
        {
            if (candidate.Manifest.Trust != ModTrust.Plugin) continue;
            try { host.LoadMod(candidate, data, api); }
            catch (Exception e)
            {
                log($"mods: '{candidate.FolderName}' could not be loaded: {e.Message}");
            }
        }
        return host;
    }

    void LoadMod(ModCandidate candidate, GameDatabase data, IModApiHost api)
    {
        var manifest = candidate.Manifest;
        if (manifest.ApiVersion != ModApi.CurrentVersion)
        {
            write($"mods: '{candidate.Id}' needs API {manifest.ApiVersion}; this build has {ModApi.CurrentVersion}.");
            return;
        }
        if (manifest.Assemblies.Count == 0 || string.IsNullOrWhiteSpace(manifest.Entry))
        {
            write($"mods: '{candidate.Id}' is a plugin but has no assemblies or entry type.");
            return;
        }

        var paths = new List<string>(manifest.Assemblies.Count);
        foreach (string assembly in manifest.Assemblies)
        {
            if (!ModPath.IsSafeRelative(assembly))
            {
                write($"mods: '{candidate.Id}' has an unsafe assembly path '{assembly}'.");
                return;
            }
            string path = GameFiles.Resolve(Path.Combine(candidate.Folder, assembly.Replace('\\', '/')));
            if (!File.Exists(path))
            {
                write($"mods: '{candidate.Id}' is missing assembly '{assembly}'.");
                return;
            }
            paths.Add(path);
        }

        var context = new ModLoadContext("Mod:" + candidate.Id, candidate.Folder);
        var assemblies = new List<Assembly>(paths.Count);
        foreach (string path in paths) assemblies.Add(context.LoadPlugin(path));

        string entryName = manifest.Entry.Split(',')[0].Trim();
        Type? entry = null;
        foreach (var assembly in assemblies)
        {
            entry = assembly.GetType(entryName, throwOnError: false);
            if (entry is not null) break;
        }
        if (entry is null)
        {
            write($"mods: '{candidate.Id}' entry type '{entryName}' was not found.");
            return;
        }
        if (!typeof(IMod).IsAssignableFrom(entry))
        {
            write($"mods: '{candidate.Id}' entry type '{entryName}' does not implement IMod.");
            return;
        }
        if (Activator.CreateInstance(entry) is not IMod mod)
        {
            write($"mods: '{candidate.Id}' entry type '{entryName}' could not be constructed.");
            return;
        }

        var modContext = new ModContext(candidate, data, services, api, write, this);
        try { mod.Initialize(modContext); }
        catch
        {
            foreach (string key in modContext.ProvidedServiceKeys) services.Remove(key);
            api.RemoveBounties(candidate.Id);
            throw;
        }
        var session = new ModSession
        {
            Candidate = candidate,
            LoadContext = context,
            Instance = mod,
            Systems = [.. modContext.Systems],
            Saveables = [.. modContext.Saveables],
        };
        sessions.Add(session);
        systemFactories.AddRange(session.Systems);
        saveables.AddRange(session.Saveables);
        write($"mods: loaded '{candidate.Id}' ({session.Systems.Count} system(s), {session.Saveables.Count} saveable(s))");
    }

    /// <summary>Appends each registered factory's system to the world's standard wiring, in mod and registration order.</summary>
    public void AddSystems(StandardSystemSet systems)
    {
        foreach (var factory in systemFactories)
        {
            try { systems.AddSystem(factory()); }
            catch (Exception e) { write($"mods: a system factory failed: {e.Message}"); }
        }
    }

    /// <summary>Points commands at a new world and flushes anything enqueued before one existed.</summary>
    public void AttachWorld(World value)
    {
        List<SimCommand> flush;
        lock (gate)
        {
            world = value;
            flush = [.. pendingCommands];
            pendingCommands.Clear();
        }
        foreach (var command in flush) value.Commands.Enqueue(command);
    }

    public void Enqueue(SimCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (gate)
        {
            if (world is { } current) current.Commands.Enqueue(command);
            else pendingCommands.Add(command);
        }
    }

    /// <summary>Serializes every loaded mod's saveables on the calling (tick) thread; write the result with <see cref="WriteSidecars"/>.</summary>
    public IReadOnlyDictionary<string, byte[]> CaptureSidecars()
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var session in sessions)
        {
            if (session.Saveables.Count == 0) continue;
            try { result[session.Id] = ModSidecar.Capture(session.Saveables); }
            catch (Exception e) { write($"mods: '{session.Id}' state could not be captured: {e.Message}"); }
        }
        return result;
    }

    public void WriteSidecars(string saveFolder, IReadOnlyDictionary<string, byte[]> sidecars)
    {
        if (sidecars.Count == 0) return;
        string folder = Path.Combine(saveFolder, "meitou-mods");
        try { Directory.CreateDirectory(folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            write($"mods: could not create '{folder}': {e.Message}");
            return;
        }
        foreach (var (id, bytes) in sidecars)
        {
            if (!ModId.IsValid(id))
            {
                write($"mods: refusing to write state for invalid mod ID '{id}'.");
                continue;
            }
            try { File.WriteAllBytes(Path.Combine(folder, id + ".bin"), bytes); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                write($"mods: could not write state for '{id}': {e.Message}");
            }
        }
    }

    /// <summary>Reads the sidecars of the mods loaded in this session and applies each saveable's payload.</summary>
    public void ReadSidecars(string saveFolder)
    {
        string folder = Path.Combine(saveFolder, "meitou-mods");
        if (!Directory.Exists(folder)) return;
        foreach (var session in sessions)
        {
            if (session.Saveables.Count == 0) continue;
            string path = Path.Combine(folder, session.Id + ".bin");
            if (!File.Exists(path)) continue;
            try
            {
                if (new FileInfo(path).Length > ModSidecar.MaxBytes)
                {
                    write($"mods: '{session.Id}' sidecar is too large.");
                    continue;
                }
                var payloads = ModSidecar.Read(File.ReadAllBytes(path));
                foreach (var saveable in session.Saveables)
                {
                    if (!payloads.TryGetValue(saveable.Key, out byte[]? bytes))
                    {
                        write($"mods: '{session.Id}' sidecar has no state for '{saveable.Key}'.");
                        continue;
                    }
                    using var stream = new MemoryStream(bytes, writable: false);
                    saveable.Read(stream);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or EndOfStreamException)
            {
                write($"mods: '{session.Id}' state could not be read: {e.Message}");
            }
        }
    }
}
