using Meitou.Data;
using Meitou.Data.Fcs;
using Meitou.Modding;
using Meitou.Simulation;
using Meitou.Simulation.Bounties;
using MoonSharp.Interpreter;

namespace Meitou.Modding.Scripting;

/// <summary>One script mod and its hard-sandboxed interpreter. All calls stay on the thread that owns the script.</summary>
sealed class LuaMod
{
    const int MaxLogMessageLength = 4096;
    static readonly string[] Events = ["world.start", "world.tick", "bounty.accepted", "bounty.completed"];

    readonly Action<string> write;
    readonly GameDatabase data;
    readonly BountyContractCatalog contracts;
    readonly Script script;
    readonly Dictionary<string, List<DynValue>> callbacks = new(StringComparer.Ordinal);

    internal LuaMod(ModCandidate candidate, GameDatabase data, BountyContractCatalog contracts, Action<string> write, string source)
    {
        Id = candidate.Id;
        this.data = data;
        this.contracts = contracts;
        this.write = write;
        script = new Script(CoreModules.Preset_HardSandbox);
        script.Options.DebugPrint = Log;
        script.Options.UseLuaErrorLocations = true;
        script.Globals["meitou"] = Api(candidate);
        script.DoString(source, null, candidate.Manifest.Entry ?? "script.lua");
    }

    public string Id { get; }
    public int CallbackCount => callbacks.Values.Sum(list => list.Count);
    public bool Active { get; private set; } = true;

    internal void Fire(string eventName, Action<Table> fill)
    {
        if (!Active || !callbacks.TryGetValue(eventName, out var handlers) || handlers.Count == 0) return;
        var payload = new Table(script);
        fill(payload);
        var argument = DynValue.NewTable(payload);
        foreach (var handler in handlers.ToArray())
        {
            try { script.Call(handler, argument); }
            catch (Exception e)
            {
                Disable($"event '{eventName}' failed: {e.Message}");
                return;
            }
        }
    }

    void Disable(string message)
    {
        if (!Active) return;
        Active = false;
        callbacks.Clear();
        contracts.RemoveSource(Id);
        Log("disabled: " + message);
    }

    void Log(string message) => write($"mods: [{Id}] {Clip(message)}");

    static string Clip(string message) => message.Length <= MaxLogMessageLength ? message : message[..MaxLogMessageLength] + "…";

    Table Api(ModCandidate candidate)
    {
        var api = new Table(script)
        {
            ["mod_id"] = Id,
            ["version"] = candidate.Manifest.Version,
            ["api_version"] = ModApi.CurrentVersion,
            ["random_salt"] = Rng.StableHash(Id).ToString("x16"),
        };

        var log = new Table(script)
        {
            ["info"] = (Action<DynValue>)(value => Log(value.ToPrintString())),
            ["warn"] = (Action<DynValue>)(value => Log("warning: " + value.ToPrintString())),
            ["error"] = (Action<DynValue>)(value => Log("error: " + value.ToPrintString())),
        };
        api["log"] = log;

        var events = new Table(script)
        {
            ["on"] = (Action<string, DynValue>)((name, handler) =>
            {
                if (!Events.Contains(name, StringComparer.Ordinal))
                    throw new ScriptRuntimeException($"meitou.events.on: unknown event '{name}'.");
                if (handler.Type != DataType.Function)
                    throw new ScriptRuntimeException("meitou.events.on: the handler must be a function.");
                if (!callbacks.TryGetValue(name, out var list)) callbacks[name] = list = [];
                list.Add(handler);
            }),
        };
        api["events"] = events;

        var content = new Table(script)
        {
            ["register"] = (Func<Table, string>)RegisterContract,
        };
        api["contracts"] = content;
        return api;
    }

    string RegisterContract(Table table)
    {
        string localId = Text(table, "id");
        if (localId.Length is 0 or > 64 || localId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-' or '.')))
            throw new ScriptRuntimeException("meitou.contracts.register: id must use letters, digits, '_', '-' or '.'.");

        var kind = Text(table, "kind") switch
        {
            "faction" => BountyTargetKind.Faction,
            "animal" => BountyTargetKind.AnimalSpecies,
            "spawned" => BountyTargetKind.Spawned,
            var other => throw new ScriptRuntimeException($"meitou.contracts.register: unknown kind '{other}'."),
        };
        var objective = Text(table, "objective", "kill") switch
        {
            "kill" => BountyObjective.Kill,
            "capture" => BountyObjective.Capture,
            var other => throw new ScriptRuntimeException($"meitou.contracts.register: unknown objective '{other}'."),
        };

        string factionId = Text(table, "faction");
        string record = Text(table, "record");
        if (kind is BountyTargetKind.Faction or BountyTargetKind.Spawned)
            factionId = ResolveRecord(factionId, FcsRecordType.FACTION, "faction").StringId;
        if (kind == BountyTargetKind.AnimalSpecies)
        {
            var animal = ResolveRecord(record, FcsRecordType.ANIMAL_CHARACTER, "record");
            if (UniqueCharacters.IsUnique(animal))
                throw new ScriptRuntimeException($"meitou.contracts.register: animal '{animal.StringId}' is unique.");
            record = animal.StringId;
        }
        if (kind == BountyTargetKind.Spawned)
        {
            var character = ResolveRecord(record, FcsRecordType.CHARACTER, "record");
            if (UniqueCharacters.IsUnique(character))
                throw new ScriptRuntimeException($"meitou.contracts.register: character '{character.StringId}' is unique.");
            record = character.StringId;
        }

        string id = $"{Id}:bounty/{localId}";
        var definition = new BountyContractDefinition
        {
            Id = id,
            Kind = kind,
            Objective = objective,
            Title = Text(table, "title", localId),
            Detail = Text(table, "detail"),
            TargetRecord = kind is BountyTargetKind.AnimalSpecies or BountyTargetKind.Spawned ? record : "",
            FactionId = kind is BountyTargetKind.Faction or BountyTargetKind.Spawned ? factionId : "",
            Required = Int(table, "required", 1),
            Reward = Int(table, "reward", 0),
        };
        contracts.Register(Id, definition);
        return id;
    }

    GameRecord ResolveRecord(string value, FcsRecordType type, string label)
    {
        if (value.Length == 0)
            throw new ScriptRuntimeException($"meitou.contracts.register: {label} is required.");
        if (data.Find(value) is { } exact)
        {
            if (exact.Type == type) return exact;
            throw new ScriptRuntimeException($"meitou.contracts.register: {label} '{value}' is a {exact.Type}, not a {type}.");
        }
        var matches = data.OfType(type).Where(record => record.Name == value).Take(2).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ScriptRuntimeException($"meitou.contracts.register: no {type} named or identified by '{value}'."),
            _ => throw new ScriptRuntimeException($"meitou.contracts.register: {label} '{value}' is ambiguous."),
        };
    }

    static string Text(Table table, string key, string fallback = "")
    {
        var value = table.Get(key);
        if (value.IsNil()) return fallback;
        if (value.Type != DataType.String)
            throw new ScriptRuntimeException($"meitou.contracts.register: '{key}' must be a string.");
        return value.String;
    }

    static int Int(Table table, string key, int fallback)
    {
        var value = table.Get(key);
        if (value.IsNil()) return fallback;
        if (value.Type != DataType.Number || !double.IsFinite(value.Number) || value.Number != Math.Truncate(value.Number) ||
            value.Number < int.MinValue || value.Number > int.MaxValue)
            throw new ScriptRuntimeException($"meitou.contracts.register: '{key}' must be an integer.");
        return (int)value.Number;
    }
}
