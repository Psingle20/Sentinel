using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Reference.Common.Telemetry;

namespace Reference.Checkout;

public static class CheckoutRevisions
{
    public const string ServiceName = "checkout-service";
    public const string Baseline = "v2.4.0";

    /// <summary>S1's cause: warms each session on a dedicated Redis connection that is never returned.</summary>
    public const string ConnectionLeak = "v2.4.1";

    public static readonly IReadOnlyCollection<string> All = [Baseline, ConnectionLeak, "v2.4.2", "v2.4.3"];
}

public sealed record CheckoutRequest(string CartId);

public sealed record CheckoutResult(string CartId, string Outcome, string Revision);

/// <summary>Checkout's metrics and traces. Every measurement carries the serving revision.</summary>
public sealed class CheckoutTelemetry : IDisposable
{
    public const string SourceName = "Reference.Checkout";

    private readonly Meter _meter = new(SourceName);

    public CheckoutTelemetry(CheckoutRedis redis)
    {
        ArgumentNullException.ThrowIfNull(redis);

        Requests = _meter.CreateCounter<long>("reference.checkout.requests", "{request}", "Checkout requests by outcome.");
        Duration = _meter.CreateHistogram("reference.checkout.duration", "s", "End-to-end checkout duration.", tags: null, LatencyBuckets.Seconds);
        OrderWait = _meter.CreateHistogram("reference.checkout.order.wait", "s", "Time checkout spends waiting on order-service, per attempt.", tags: null, LatencyBuckets.Seconds);
        OrderRetries = _meter.CreateCounter<long>("reference.checkout.order.retries", "{retry}", "Retried order-service calls.");
        RedisErrors = _meter.CreateCounter<long>("reference.checkout.redis.errors", "{error}", "Failed Redis operations.");
        _meter.CreateObservableGauge("reference.checkout.redis.pool.connections", () => redis.PoolConnections, "{connection}", "Open pooled Redis connections.");
        _meter.CreateObservableGauge("reference.checkout.redis.leaked.connections", () => redis.LeakedConnections, "{connection}", "Redis connections opened and never returned.");
    }

    public static ActivitySource Activities { get; } = new(SourceName);

    public Counter<long> Requests { get; }

    public Histogram<double> Duration { get; }

    public Histogram<double> OrderWait { get; }

    public Counter<long> OrderRetries { get; }

    public Counter<long> RedisErrors { get; }

    public void Dispose() => _meter.Dispose();
}

/// <summary>Checkout's Redis use: a pool for cart writes and, in v2.4.1, a leaked connection per warmed session.</summary>
public sealed class CheckoutRedis : IAsyncDisposable
{
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromMilliseconds(500);

    private readonly RedisEndpoint _endpoint;
    private readonly RedisConnectionPool _pool;
    private readonly ConcurrentQueue<RespConnection> _leaked = new();
    private long _sessions;

    public CheckoutRedis(RedisEndpoint endpoint, RevisionState revision, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(revision);

        _endpoint = endpoint;
        _pool = new RedisConnectionPool(endpoint, CheckoutRevisions.ServiceName, size: 4, maxLifetime: TimeSpan.FromSeconds(5), CommandTimeout, time);
        revision.OnChanged(OnRevisionChangedAsync);
    }

    public int PoolConnections => _pool.OpenConnections;

    public int LeakedConnections => _leaked.Count;

    public async Task TouchSessionAsync(string cartId, string revision, CancellationToken cancellationToken)
    {
        using Activity? activity = CheckoutTelemetry.Activities.StartActivity("redis session", ActivityKind.Client);
        activity?.SetTag("db.system.name", "redis");

        // v2.4.1 warms every second session on its own connection and forgets to close it.
        if (revision == CheckoutRevisions.ConnectionLeak && Interlocked.Increment(ref _sessions) % 2 == 0)
        {
            RespConnection leaked = await RespConnection.OpenAsync(_endpoint, CheckoutRevisions.ServiceName, CommandTimeout, cancellationToken);
            _leaked.Enqueue(leaked);
            await leaked.ExecuteAsync(CommandTimeout, cancellationToken, "SET", $"session:{cartId}", "warm", "EX", "300");
        }

        await _pool.ExecuteAsync(cancellationToken, "SET", $"cart:{cartId}", "checked-out", "EX", "300");
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync();
        await _pool.DisposeAsync();
    }

    // A revision switch emulates a new process: everything the old revision held is closed.
    private Task OnRevisionChangedAsync(RevisionChange change) => ReleaseAsync();

    private async Task ReleaseAsync()
    {
        while (_leaked.TryDequeue(out RespConnection? connection))
        {
            await connection.DisposeAsync();
        }

        await _pool.ResetAsync();
    }
}

public sealed record OrderRequest(string CartId);

public sealed class OrderServiceClient(HttpClient http)
{
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromMilliseconds(600);

    public static Uri ResolveBaseAddress(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string address = configuration["services:order-service:http:0"]
            ?? configuration["Reference:OrderService"]
            ?? throw new InvalidOperationException("The order-service address is not configured.");
        return new Uri(address);
    }

    public async Task<bool> PlaceOrderAsync(string cartId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AttemptTimeout);
        try
        {
            using HttpResponseMessage response = await http.PostAsJsonAsync("/api/orders", new OrderRequest(cartId), timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}

/// <summary>
/// One checkout: write the cart to Redis, then place the order, retrying order-service up to
/// three times. The retries are what amplify an order-service slowdown in S1 and S3.
/// </summary>
public sealed class CheckoutProcessor(
    CheckoutRedis redis,
    OrderServiceClient orders,
    RevisionState revision,
    CheckoutTelemetry telemetry,
    TimeProvider time,
    ILogger<CheckoutProcessor> logger)
{
    public const int MaxOrderAttempts = 3;

    public static readonly TimeSpan SlowThreshold = TimeSpan.FromSeconds(1);

    public async Task<CheckoutResult> ProcessAsync(string cartId, CancellationToken cancellationToken)
    {
        long started = time.GetTimestamp();
        string serving = revision.Current;
        var tags = new TagList { { "revision", serving } };
        string outcome;

        try
        {
            await redis.TouchSessionAsync(cartId, serving, cancellationToken);
            outcome = await PlaceOrderWithRetriesAsync(cartId, tags, cancellationToken) ? "success" : "order_failed";
        }
        catch (RedisUnavailableException exception)
        {
            telemetry.RedisErrors.Add(1, tags);
            logger.LogError(exception, "Checkout {CartId} failed on Redis: {Reason}", cartId, exception.InnerException?.Message ?? exception.Message);
            outcome = "redis_error";
        }

        TimeSpan elapsed = time.GetElapsedTime(started);
        if (outcome == "success" && elapsed > SlowThreshold)
        {
            outcome = "slow";
        }

        tags.Add("outcome", outcome);
        telemetry.Requests.Add(1, tags);
        telemetry.Duration.Record(elapsed.TotalSeconds, tags);
        return new CheckoutResult(cartId, outcome, serving);
    }

    private async Task<bool> PlaceOrderWithRetriesAsync(string cartId, TagList tags, CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxOrderAttempts; attempt++)
        {
            if (attempt > 1)
            {
                telemetry.OrderRetries.Add(1, tags);
            }

            long waitStarted = time.GetTimestamp();
            bool placed = await orders.PlaceOrderAsync(cartId, cancellationToken);
            telemetry.OrderWait.Record(time.GetElapsedTime(waitStarted).TotalSeconds, tags);
            if (placed)
            {
                return true;
            }
        }

        logger.LogWarning("Checkout {CartId} gave up on order-service after {Attempts} attempts", cartId, MaxOrderAttempts);
        return false;
    }
}
