using Meitou.Modding.Api;
using Meitou.Simulation;
using Meitou.Simulation.Bounties;

namespace Frontier.Bounties;

/// <summary>
/// The frontier bounty framework. It owns the one contract catalog the game hands to the bounty board and the Lua host; content packs
/// publish offers either as C# definitions or through the Lua API. The sample keeps its own save state so a save sidecar is exercised too.
/// </summary>
public sealed class Plugin : IMod
{
    public void Initialize(IModContext context)
    {
        var catalog = new BountyContractCatalog();
        context.Services.Provide(ModServiceKeys.BountyContracts, catalog);

        var heartbeat = new Heartbeat(context.Log);
        context.AddSystem(() => heartbeat);
        context.AddSaveable(heartbeat);
        context.Log.Info("contract catalog ready; C# and Lua content share it");
    }
}

sealed class Heartbeat(IModLog log) : ITickSystem, ISaveable
{
    long ticks;

    public string Key => "ticks";

    public void SlowWorld(World world)
    {
        ticks = world.Tick;
        if (ticks != 0 && ticks % 1_000_000 == 0) log.Info($"heartbeat at tick {ticks}");
    }

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(ticks);
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        ticks = reader.ReadInt64();
        if (ticks < 0) throw new InvalidDataException("Negative heartbeat tick count.");
    }
}
