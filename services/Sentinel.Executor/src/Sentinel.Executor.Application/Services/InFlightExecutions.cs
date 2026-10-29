using System.Collections.Concurrent;

namespace Sentinel.Executor.Application.Services;

/// <summary>
/// The execution requests this process is handling right now. Reconciliation finishes what a crash left behind, and a
/// request still being handled here was not left behind, however long a step such as inspecting the target takes; without
/// this, a slow step let reconciliation reject a live request as abandoned. Requests of a process that died are not here,
/// so they are reconciled as before.
/// </summary>
public sealed class InFlightExecutions
{
    private readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.Ordinal);

    /// <summary>Marks the request as being handled until the returned handle is disposed.</summary>
    public IDisposable Track(string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        _keys.TryAdd(idempotencyKey, 0);
        return new Handle(this, idempotencyKey);
    }

    public bool Contains(string idempotencyKey) => _keys.ContainsKey(idempotencyKey);

    private sealed class Handle(InFlightExecutions owner, string idempotencyKey) : IDisposable
    {
        public void Dispose() => owner._keys.TryRemove(idempotencyKey, out _);
    }
}
