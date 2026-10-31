using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Reference.Common.Telemetry;
using Reference.Checkout.Revisions;
using Reference.Checkout.Telemetry;

namespace Reference.Checkout.Redis;

/// <summary>Checkout's Redis use: a pool for cart writes and, in v2.4.1, a leaked connection per warmed session.</summary>
public sealed class CheckoutRedis : IAsyncDisposable
{
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromMilliseconds(500);

    private readonly RedisEndpoint _endpoint;
    private readonly RedisConnectionPool _pool;
    private readonly CheckoutSessionLeak _leak = new();

    public CheckoutRedis(RedisEndpoint endpoint, RevisionState revision, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(revision);

        _endpoint = endpoint;
        _pool = new RedisConnectionPool(endpoint, CheckoutRevisions.ServiceName, size: 4, maxLifetime: TimeSpan.FromSeconds(5), CommandTimeout, time);
        revision.OnChanged(OnRevisionChangedAsync);
    }

    public int PoolConnections => _pool.OpenConnections;

    public int LeakedConnections => _leak.Count;

    public async Task TouchSessionAsync(string cartId, string revision, CancellationToken cancellationToken)
    {
        using Activity? activity = CheckoutTelemetry.Activities.StartActivity("redis session", ActivityKind.Client);
        activity?.SetTag("db.system.name", "redis");

        await _leak.WarmAsync(_endpoint, cartId, revision, cancellationToken);

        await _pool.ExecuteAsync(cancellationToken, "SET", $"cart:{cartId}", "checked-out", "EX", "300");
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync();
        await _pool.DisposeAsync();
    }

    // A revision switch stands for a new process: everything the old revision held is closed.
    private Task OnRevisionChangedAsync(RevisionChange change) => ReleaseAsync();

    private async Task ReleaseAsync()
    {
        await _leak.ReleaseAsync();
        await _pool.ResetAsync();
    }
}
