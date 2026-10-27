using Meitou.Modding.Api;

namespace Meitou.Modding.Runtime;

/// <summary>
/// One typed event bus shared by the C# and Lua hosts. Handlers run synchronously on the publisher's (simulation) thread in registration order.
/// A handler's exception is logged and does not stop the remaining handlers, the board or the world.
/// </summary>
public sealed class ModEventBus : IModEvents
{
    readonly Action<string>? write;
    readonly object gate = new();
    readonly Dictionary<Type, List<object>> handlers = [];

    public ModEventBus(Action<string>? write = null) => this.write = write;

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (gate)
        {
            if (!handlers.TryGetValue(typeof(TEvent), out var list)) handlers[typeof(TEvent)] = list = [];
            list.Add(handler);
        }
        return new Subscription(this, typeof(TEvent), handler);
    }

    /// <summary>Publishes one event to the handlers registered when the publish starts.</summary>
    public void Publish<TEvent>(TEvent value)
    {
        object[] snapshot;
        lock (gate)
        {
            if (!handlers.TryGetValue(typeof(TEvent), out var list) || list.Count == 0) return;
            snapshot = [.. list];
        }
        foreach (object handler in snapshot)
        {
            try { ((Action<TEvent>)handler)(value); }
            catch (Exception e)
            {
                write?.Invoke($"mods: {typeof(TEvent).Name} handler failed: {e.GetBaseException().Message}");
            }
        }
    }

    void Remove(Type eventType, object handler)
    {
        lock (gate)
        {
            if (!handlers.TryGetValue(eventType, out var list)) return;
            list.Remove(handler);
            if (list.Count == 0) handlers.Remove(eventType);
        }
    }

    sealed class Subscription(ModEventBus owner, Type eventType, object handler) : IDisposable
    {
        int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            owner.Remove(eventType, handler);
        }
    }
}
