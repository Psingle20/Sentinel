using System.Collections.Concurrent;
using Reference.Common.Redis;
using Reference.Checkout.Revisions;

namespace Reference.Checkout.Redis;

/// <summary>
/// The S1 fault: v2.4.1 warms every second session on its own Redis connection and forgets to close
/// it, so connections climb until Redis refuses checkout. A revision switch closes them all, as a new
/// process would.
/// </summary>
public sealed class CheckoutSessionLeak
{
    private readonly ConcurrentQueue<RespConnection> _leaked = new();
    private long _sessions;

    public int Count => _leaked.Count;

    public async Task WarmAsync(RedisEndpoint endpoint, string cartId, string revision, CancellationToken cancellationToken)
    {
        if (revision == CheckoutRevisions.ConnectionLeak && Interlocked.Increment(ref _sessions) % 2 == 0)
        {
            RespConnection leaked = await RespConnection.OpenAsync(endpoint, CheckoutRevisions.ServiceName, CheckoutRedis.CommandTimeout, cancellationToken);
            _leaked.Enqueue(leaked);
            await leaked.ExecuteAsync(CheckoutRedis.CommandTimeout, cancellationToken, "SET", $"session:{cartId}", "warm", "EX", "300");
        }
    }

    public async Task ReleaseAsync()
    {
        while (_leaked.TryDequeue(out RespConnection? connection))
        {
            await connection.DisposeAsync();
        }
    }
}
