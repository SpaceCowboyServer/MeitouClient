using System.Collections.Concurrent;
using Meitou.Modding.Api;

namespace Meitou.Modding.Plugins;

/// <summary>String-keyed services shared by the loaded plugins. A duplicate key fails the later plugin's initialization.</summary>
sealed class ModServices : IModServices
{
    readonly ConcurrentDictionary<string, object> services = new(StringComparer.Ordinal);

    public void Provide(string key, object service)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(service);
        if (!services.TryAdd(key, service))
            throw new InvalidOperationException($"Service '{key}' is already provided.");
    }

    public bool TryGet(string key, out object? service) => services.TryGetValue(key, out service);

    public void Remove(string key) => services.TryRemove(key, out _);
}
