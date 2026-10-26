namespace Meitou.Simulation.Bounties;

/// <summary>
/// A contract offer contributed by a mod or script. The board resolves faction and record IDs when it builds the offer; the definition itself keeps only
/// stable content data, so it can be registered before any world exists.
/// </summary>
public sealed record BountyContractDefinition
{
    public required string Id { get; init; }
    public required BountyTargetKind Kind { get; init; }
    public required BountyObjective Objective { get; init; }
    public required string Title { get; init; }
    public string Detail { get; init; } = "";
    /// <summary>The animal record for an animal cull, or the character record for a spawned target.</summary>
    public string TargetRecord { get; init; } = "";
    /// <summary>The FACTION string ID for a faction contract or a spawned target's faction.</summary>
    public string FactionId { get; init; } = "";
    public int Required { get; init; } = 1;
    public int Reward { get; init; }
    /// <summary>The mod or script that registered the definition; set by <see cref="BountyContractCatalog"/>.</summary>
    public string Source { get; init; } = "";
}

/// <summary>
/// The live registry of contract definitions contributed by mods. It is passed to <see cref="BountyBoardSystem"/> and read on every offer refresh, so a
/// script may register content during initialization or later. Definitions are not saved: the board's accepted/completed contracts are.
/// </summary>
public sealed class BountyContractCatalog
{
    const int MaxDefinitions = 1024;
    const int MaxIdLength = 160;
    const int MaxTitleLength = 200;
    const int MaxDetailLength = 300;
    const int MaxRequired = 10000;
    const int MaxReward = 100_000_000;

    readonly object gate = new();
    readonly Dictionary<string, BountyContractDefinition> definitions = new(StringComparer.Ordinal);

    public IReadOnlyList<BountyContractDefinition> Definitions
    {
        get
        {
            lock (gate) return [.. definitions.Values.OrderBy(definition => definition.Id, StringComparer.Ordinal)];
        }
    }

    public bool TryGet(string id, out BountyContractDefinition definition)
    {
        lock (gate) return definitions.TryGetValue(id, out definition!);
    }

    /// <summary>Registers one definition; a duplicate ID or invalid shape throws, and the caller isolates that mod.</summary>
    public void Register(string source, BountyContractDefinition definition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(definition);
        Validate(definition);
        lock (gate)
        {
            if (definitions.Count >= MaxDefinitions)
                throw new InvalidOperationException($"The contract catalog is full ({MaxDefinitions} definitions).");
            if (!definitions.TryAdd(definition.Id, definition with { Source = source }))
                throw new InvalidOperationException($"Contract ID '{definition.Id}' is already registered.");
        }
    }

    /// <summary>Removes every definition a mod registered; returns how many were removed.</summary>
    public int RemoveSource(string source)
    {
        lock (gate)
        {
            var remove = definitions.Values.Where(definition => definition.Source == source).Select(definition => definition.Id).ToList();
            foreach (string id in remove) definitions.Remove(id);
            return remove.Count;
        }
    }

    static void Validate(BountyContractDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Id) || definition.Id.Length > MaxIdLength || definition.Id.Any(char.IsWhiteSpace))
            throw new ArgumentException("A contract definition needs a short ID without whitespace.", nameof(definition));
        if (string.IsNullOrWhiteSpace(definition.Title) || definition.Title.Length > MaxTitleLength)
            throw new ArgumentException("A contract definition needs a title.", nameof(definition));
        if (definition.Detail is { Length: > MaxDetailLength })
            throw new ArgumentException("A contract detail is too long.", nameof(definition));
        if (definition.Required is < 1 or > MaxRequired)
            throw new ArgumentException($"A contract needs between 1 and {MaxRequired} required actions.", nameof(definition));
        if (definition.Reward is < 0 or > MaxReward)
            throw new ArgumentException($"A contract reward must be between 0 and {MaxReward}.", nameof(definition));
        if (!Enum.IsDefined(definition.Kind) || !Enum.IsDefined(definition.Objective))
            throw new ArgumentException("A contract has an unknown target kind or objective.", nameof(definition));
        if (definition.Kind == BountyTargetKind.Character)
            throw new ArgumentException("A content contract cannot name one existing character yet; use faction, animal or spawned.", nameof(definition));
        if (definition.Kind == BountyTargetKind.Faction && string.IsNullOrWhiteSpace(definition.FactionId))
            throw new ArgumentException("A faction contract needs a faction ID.", nameof(definition));
        if (definition.Kind == BountyTargetKind.AnimalSpecies && string.IsNullOrWhiteSpace(definition.TargetRecord))
            throw new ArgumentException("An animal contract needs an animal record.", nameof(definition));
        if (definition.Kind == BountyTargetKind.Spawned &&
            (string.IsNullOrWhiteSpace(definition.FactionId) || string.IsNullOrWhiteSpace(definition.TargetRecord)))
            throw new ArgumentException("A spawned contract needs a faction ID and a character record.", nameof(definition));
        if (definition.Objective == BountyObjective.Capture && definition.Kind != BountyTargetKind.Spawned)
            throw new ArgumentException("Capture is only available for spawned targets.", nameof(definition));
    }
}
