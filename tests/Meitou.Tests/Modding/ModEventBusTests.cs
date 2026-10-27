using Meitou.Modding.Api;
using Meitou.Modding.Runtime;

namespace Meitou.Tests.Modding;

public class ModEventBusTests
{
    [Fact]
    public void Handlers_run_in_registration_order_and_unsubscribe()
    {
        var bus = new ModEventBus();
        var seen = new List<int>();
        using var first = bus.Subscribe<WorldTick>(_ => seen.Add(1));
        using var second = bus.Subscribe<WorldTick>(_ => seen.Add(2));

        bus.Publish(new WorldTick(1, 1, 0, 0));
        Assert.Equal([1, 2], seen);

        second.Dispose();
        bus.Publish(new WorldTick(1, 2, 0, 0));
        Assert.Equal([1, 2, 1], seen);
    }

    [Fact]
    public void A_throwing_handler_does_not_stop_the_others()
    {
        var log = new List<string>();
        var bus = new ModEventBus(log.Add);
        int calls = 0;
        bus.Subscribe<WorldTick>(_ => throw new InvalidOperationException("event boom"));
        bus.Subscribe<WorldTick>(_ => calls++);

        bus.Publish(new WorldTick(1, 1, 0, 0));

        Assert.Equal(1, calls);
        Assert.Contains(log, line => line.Contains("event boom", StringComparison.Ordinal));
    }

    [Fact]
    public void A_subscription_added_during_a_publish_waits_for_the_next_event()
    {
        var bus = new ModEventBus();
        var seen = new List<int>();
        bool added = false;
        bus.Subscribe<WorldTick>(_ =>
        {
            seen.Add(1);
            if (added) return;
            added = true;
            bus.Subscribe<WorldTick>(_ => seen.Add(2));
        });

        bus.Publish(new WorldTick(1, 1, 0, 0));
        Assert.Equal([1], seen);

        bus.Publish(new WorldTick(1, 2, 0, 0));
        Assert.Equal([1, 1, 2], seen);
    }
}
