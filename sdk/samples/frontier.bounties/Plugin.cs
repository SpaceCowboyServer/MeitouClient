using Meitou.Modding.Api;

namespace Frontier.Bounties;

/// <summary>
/// The frontier bounty framework. It registers a contract through the stable bounty API and subscribes to the shared accepted/completed events;
/// it also keeps a small saveable heartbeat so loading it exercises the sidecar path.
/// </summary>
public sealed class Plugin : IMod
{
    public void Initialize(IModContext context)
    {
        var id = context.Bounties.Register(new BountyDefinition(
            LocalId: "dust-bandit-patrol",
            Target: BountyTarget.Faction,
            Goal: BountyGoal.Kill,
            Title: "Dust Bandit Patrol",
            Detail: "Kill one Dust Bandit for the frontier board.",
            FactionId: "200-gamedata.base",   // FACTION "Dust Bandits"
            Required: 1,
            Reward: 1500));

        var heartbeat = new Heartbeat(context.Log);
        context.Events.Subscribe<WorldTick>(heartbeat.Tick);
        context.Events.Subscribe<BountyAccepted>(bounty => context.Log.Info($"accepted {bounty.Title} ({bounty.Id})"));
        context.Events.Subscribe<BountyCompleted>(bounty => context.Log.Info($"completed {bounty.Title} for {bounty.Reward} cats"));
        context.AddSaveable(heartbeat);
        context.Log.Info($"registered {id}");
    }
}

sealed class Heartbeat(IModLog log) : ISaveable
{
    long ticks;

    public string Key => "ticks";

    public void Tick(WorldTick world)
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
