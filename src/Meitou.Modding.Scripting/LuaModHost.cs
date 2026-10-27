using Meitou.Content;
using Meitou.Data;
using Meitou.Modding;
using Meitou.Modding.Api;
using MoonSharp.Interpreter;

namespace Meitou.Modding.Scripting;

/// <summary>
/// Loads enabled <c>trust: script</c> mods after the data merge, in resolution order. Each script gets its own hard-sandboxed MoonSharp interpreter;
/// a syntax/runtime error, a bad registration or a throwing event handler disables only that script and logs it. Scripts register contracts and
/// receive events through the same <see cref="IModApiHost"/> as the C# plugins.
/// </summary>
public sealed class LuaModHost
{
    public const int MaxScriptBytes = 1024 * 1024;

    readonly Action<string> write;
    readonly List<LuaMod> scripts = [];
    readonly IModApiHost api;

    LuaModHost(IModApiHost api, Action<string> write)
    {
        this.api = api;
        this.write = write;
        api.Events.Subscribe<WorldStarted>(started => FireAll("world.start", table => FillWorld(table, started)));
        api.Events.Subscribe<WorldTick>(tick => FireAll("world.tick", table => FillWorld(table, tick)));
        api.Events.Subscribe<BountyAccepted>(accepted => FireAll("bounty.accepted", table => FillBounty(table, accepted)));
        api.Events.Subscribe<BountyCompleted>(completed => FireAll("bounty.completed", table => FillBounty(table, completed)));
    }

    public int ScriptCount => scripts.Count;
    public IReadOnlyList<string> LoadedModIds => [.. scripts.Select(script => script.Id)];

    /// <summary>Loads scripts from an already resolved set; plugins and scripts are separate hosts but share the runtime's API services.</summary>
    public static LuaModHost Load(ModResolution resolution, GameDatabase data, IModApiHost api, Action<string>? write = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        var log = write ?? Console.WriteLine;
        var host = new LuaModHost(api, log);
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
            var script = new LuaMod(candidate, data, api, write, source);
            scripts.Add(script);
            write($"mods: loaded '{candidate.Id}' ({script.CallbackCount} event handler(s))");
        }
        catch (Exception e)
        {
            api.RemoveBounties(candidate.Id);
            write($"mods: '{candidate.Id}' script failed: {e.Message}");
        }
    }

    internal void FireAll(string eventName, Action<Table> fill)
    {
        foreach (var script in scripts.ToArray())
            if (script.Active) script.Fire(eventName, fill);
    }

    static void FillWorld(Table table, WorldStarted world)
    {
        table["tick"] = world.Tick;
        table["seed"] = world.Seed.ToString("x16");
        table["money"] = world.Money;
        table["game_hours"] = world.GameHours;
    }

    static void FillWorld(Table table, WorldTick world)
    {
        table["tick"] = world.Tick;
        table["seed"] = world.Seed.ToString("x16");
        table["money"] = world.Money;
        table["game_hours"] = world.GameHours;
    }

    static void FillBounty(Table table, BountyAccepted contract)
    {
        table["id"] = contract.Id;
        table["title"] = contract.Title;
        table["target"] = contract.Target;
        table["kind"] = contract.Kind;
        table["objective"] = contract.Objective;
        table["required"] = contract.Required;
        table["reward"] = contract.Reward;
        table["progress"] = contract.Progress;
    }

    static void FillBounty(Table table, BountyCompleted contract)
    {
        table["id"] = contract.Id;
        table["title"] = contract.Title;
        table["target"] = contract.Target;
        table["kind"] = contract.Kind;
        table["objective"] = contract.Objective;
        table["required"] = contract.Required;
        table["reward"] = contract.Reward;
        table["progress"] = contract.Required;
    }
}
