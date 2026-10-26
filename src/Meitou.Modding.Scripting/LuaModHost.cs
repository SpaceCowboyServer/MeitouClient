using Meitou.Content;
using Meitou.Data;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bounties;

namespace Meitou.Modding.Scripting;

/// <summary>
/// Loads enabled <c>trust: script</c> mods after the data merge, in resolution order. Each script gets its own hard-sandboxed MoonSharp interpreter;
/// a syntax/runtime error, a bad registration or a throwing event handler disables only that script and logs it. Scripts contribute contracts through
/// <see cref="Contracts"/>; one appended per-world system runs their events and observes the built-in board.
/// </summary>
public sealed class LuaModHost
{
    public const int MaxScriptBytes = 1024 * 1024;

    readonly Action<string> write;
    readonly List<LuaMod> scripts = [];
    readonly BountyContractCatalog contracts = new();

    LuaModHost(Action<string> write) => this.write = write;

    public int ScriptCount => scripts.Count;
    public int SystemCount => scripts.Count == 0 ? 0 : 1;
    public IReadOnlyList<string> LoadedModIds => [.. scripts.Select(script => script.Id)];
    public BountyContractCatalog Contracts => contracts;

    /// <summary>Loads scripts from an already resolved set; plugins and scripts are separate hosts but share the resolution's order within their tier.</summary>
    public static LuaModHost Load(ModResolution resolution, GameDatabase data, Action<string>? write = null)
    {
        var log = write ?? Console.WriteLine;
        var host = new LuaModHost(log);
        foreach (var candidate in resolution.Order)
        {
            if (candidate.Manifest.Trust != ModTrust.Script) continue;
            host.LoadMod(candidate, data);
        }
        return host;
    }

    void LoadMod(ModCandidate candidate, GameDatabase data)
    {
        var manifest = candidate.Manifest;
        if (manifest.ApiVersion != ModApi.CurrentVersion)
        {
            write($"mods: '{candidate.Id}' needs API {manifest.ApiVersion}; this build has {ModApi.CurrentVersion}.");
            return;
        }
        if (string.IsNullOrWhiteSpace(manifest.Entry) || !ModPath.IsSafeRelative(manifest.Entry))
        {
            write($"mods: '{candidate.Id}' is a script but has no safe entry path.");
            return;
        }

        string relative = manifest.Entry.Replace('\\', '/');
        string path = GameFiles.Resolve(Path.Combine(candidate.Folder, relative));
        if (!File.Exists(path))
        {
            write($"mods: '{candidate.Id}' is missing script '{manifest.Entry}'.");
            return;
        }

        string source;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxScriptBytes)
            {
                write($"mods: '{candidate.Id}' script is larger than {MaxScriptBytes} bytes.");
                return;
            }
            source = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            write($"mods: '{candidate.Id}' script could not be read: {e.Message}");
            return;
        }

        try
        {
            var script = new LuaMod(candidate, data, contracts, write, source);
            scripts.Add(script);
            write($"mods: loaded '{candidate.Id}' ({script.CallbackCount} event handler(s), {contracts.Definitions.Count(definition => definition.Source == candidate.Id)} contract(s))");
        }
        catch (Exception e)
        {
            contracts.RemoveSource(candidate.Id);
            write($"mods: '{candidate.Id}' script failed: {e.Message}");
        }
    }

    /// <summary>Adds the one event system shared by all scripts; it runs after the standard systems (including the contract board).</summary>
    public void AddSystems(StandardSystemSet systems)
    {
        ArgumentNullException.ThrowIfNull(systems);
        if (scripts.Count > 0) systems.AddSystem(new LuaWorldSystem(this));
    }

    internal IReadOnlyList<LuaMod> Scripts => scripts;
    internal Action<string> Write => write;

    internal void FireAll(string eventName, Action<MoonSharp.Interpreter.Table> fill)
    {
        foreach (var script in scripts.ToArray())
            if (script.Active) script.Fire(eventName, fill);
    }
}
