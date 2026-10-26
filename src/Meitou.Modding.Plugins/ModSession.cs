using Meitou.Modding.Api;
using Meitou.Modding;

namespace Meitou.Modding.Plugins;

/// <summary>One loaded plugin, its load context and the registrations it made successfully.</summary>
sealed class ModSession
{
    public required ModCandidate Candidate { get; init; }
    public required ModLoadContext LoadContext { get; init; }
    public required IMod Instance { get; init; }
    public required List<Func<Meitou.Simulation.ITickSystem>> Systems { get; init; }
    public required List<ISaveable> Saveables { get; init; }

    public string Id => Candidate.Id;
}
