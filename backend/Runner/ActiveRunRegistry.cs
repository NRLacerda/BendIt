using System.Collections.Concurrent;

namespace BendIt.Api.Runner;

public sealed class ActiveRunRegistry
{
    private readonly ConcurrentDictionary<string, byte> _activeRuns = new(StringComparer.Ordinal);

    public bool Reserve(string projectId) => _activeRuns.TryAdd(projectId, 1);

    public void Release(string projectId) => _activeRuns.TryRemove(projectId, out _);
}
