using Meitou.Data;
using Meitou.Modding.Api;
using Meitou.Simulation;

namespace Meitou.Modding.Plugins;

/// <summary>The concrete context and log handed to one plugin during <see cref="IMod.Initialize"/>.</summary>
sealed class ModContext : IModContext, IModLog, IModServices
{
    readonly ModPluginHost host;
    readonly Action<string> write;
    readonly ModServices sharedServices;
    readonly List<string> providedServiceKeys = [];
    readonly List<Func<ITickSystem>> systems = [];
    readonly List<ISaveable> saveables = [];
    readonly HashSet<string> saveableKeys = new(StringComparer.Ordinal);

    public ModContext(ModCandidate candidate, GameDatabase data, ModServices services, IModApiHost api, Action<string> write, ModPluginHost host)
    {
        Id = candidate.Id;
        Version = candidate.Manifest.Version;
        Folder = candidate.Folder;
        Data = data;
        sharedServices = services;
        Events = api.Events;
        Bounties = api.BountiesFor(candidate.Id);
        Board = api.Board;
        Dialogue = api.Dialogue;
        RandomSalt = Rng.StableHash(candidate.Id);
        this.write = write;
        this.host = host;
    }

    public string Id { get; }
    public string Version { get; }
    public string Folder { get; }
    public int ApiVersion => ModApi.CurrentVersion;
    public GameDatabase Data { get; }
    public IModEvents Events { get; }
    public IBountyContracts Bounties { get; }
    public IBountyBoard Board { get; }
    public IDialogue Dialogue { get; }
    public ulong RandomSalt { get; }
    public IModLog Log => this;
    public IModServices Services => this;

    public IReadOnlyList<Func<ITickSystem>> Systems => systems;
    public IReadOnlyList<ISaveable> Saveables => saveables;
    public IReadOnlyList<string> ProvidedServiceKeys => providedServiceKeys;

    void IModServices.Provide(string key, object service)
    {
        sharedServices.Provide(key, service);
        providedServiceKeys.Add(key);
    }

    bool IModServices.TryGet(string key, out object? service) => sharedServices.TryGet(key, out service);

    public void AddSystem(Func<ITickSystem> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        systems.Add(factory);
    }

    public void AddSaveable(ISaveable saveable)
    {
        ArgumentNullException.ThrowIfNull(saveable);
        if (string.IsNullOrWhiteSpace(saveable.Key))
            throw new ArgumentException("A saveable needs a non-empty key.", nameof(saveable));
        if (!saveableKeys.Add(saveable.Key))
            throw new ArgumentException($"Saveable key '{saveable.Key}' is registered more than once.", nameof(saveable));
        saveables.Add(saveable);
    }

    public void Enqueue(SimCommand command) => host.Enqueue(command);

    public void Info(string message) => write($"mods: [{Id}] {message}");
    public void Warn(string message) => write($"mods: [{Id}] warning: {message}");
    public void Error(string message) => write($"mods: [{Id}] error: {message}");
}
