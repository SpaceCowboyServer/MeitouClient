using Meitou.Modding;
using Meitou.Modding.Api;

namespace Meitou.Tests.Modding;

/// <summary>A minimal plugin in the test assembly, copied into a temporary mod folder and loaded by a private ALC.</summary>
public sealed class TestMod : IMod
{
    public void Initialize(IModContext context)
    {
        var state = new[] { 7 };
        context.Services.Provide("test", state);
        context.AddSaveable(new TestSaveable(state));
        context.AddSystem(() => new TestSystem());
        context.Enqueue(new TestCommand { Tick = 0 });
        context.Log.Info("initialized");
    }
}

public sealed class BrokenMod : IMod
{
    public void Initialize(IModContext context) => throw new InvalidOperationException("broken on purpose");
}

public sealed class FailingAfterServiceMod : IMod
{
    public void Initialize(IModContext context)
    {
        context.Services.Provide("rolled-back", new object());
        throw new InvalidOperationException("fails after a service");
    }
}

sealed class TestSaveable(int[] state) : ISaveable
{
    public string Key => "test";

    public void Write(Stream stream)
    {
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.Write(state[0]);
    }

    public void Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        state[0] = reader.ReadInt32();
    }
}

sealed class TestSystem : Meitou.Simulation.ITickSystem;

public sealed record TestCommand : Meitou.Simulation.SimCommand;
