using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Reference.Common.Redis;

namespace Reference.ReportingWorker;

public static class ReportingRevisions
{
    public const string ServiceName = "reporting-worker";
    public const string Baseline = "v3.1.0";

    public static readonly IReadOnlyCollection<string> All = [Baseline];
}

/// <summary>Batch parameters. The defaults bring Redis close to maxclients in about a minute.</summary>
public sealed record StartBatchRequest(int TargetConnections = 190, double ConnectionsPerSecond = 3);

public sealed record BatchStatus(bool Active, int Connections, int TargetConnections, DateTimeOffset? StartedAt, string? LastError);

/// <summary>
/// The S2 confounder. A batch fans out one Redis connection per report partition and holds them
/// until it finishes or is stopped. It is not a deployment, so a deploy-blame heuristic cannot see it.
/// </summary>
public sealed class ReportingBatch(RedisEndpoint endpoint, TimeProvider time, ILogger<ReportingBatch> logger) : IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RefusedRetryDelay = TimeSpan.FromMilliseconds(20);

    private readonly ConcurrentQueue<RespConnection> _connections = new();
    private readonly Lock _gate = new();
    private CancellationTokenSource? _run;
    private Task _ramp = Task.CompletedTask;
    private StartBatchRequest _request = new();
    private DateTimeOffset? _startedAt;
    private string? _lastError;

    public int Connections => _connections.Count;

    public bool Active
    {
        get
        {
            lock (_gate)
            {
                return _run is not null;
            }
        }
    }

    public BatchStatus Status()
    {
        lock (_gate)
        {
            return new BatchStatus(_run is not null, _connections.Count, _request.TargetConnections, _startedAt, _lastError);
        }
    }

    /// <summary>Starts a batch; starting while one runs leaves it unchanged.</summary>
    public BatchStatus Start(StartBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            if (_run is null)
            {
                _request = request;
                _startedAt = time.GetUtcNow();
                _lastError = null;
                _run = new CancellationTokenSource();
                _ramp = RampAsync(request, _run.Token);
                logger.LogInformation("Reporting batch started: target {Target} connections at {Rate}/s", request.TargetConnections, request.ConnectionsPerSecond);
            }
        }

        return Status();
    }

    /// <summary>Stops the batch and closes every connection it opened. Idempotent.</summary>
    public async Task<BatchStatus> StopAsync()
    {
        CancellationTokenSource? run;
        Task ramp;
        lock (_gate)
        {
            run = _run;
            ramp = _ramp;
            _run = null;
            _startedAt = null;
        }

        if (run is not null)
        {
            await run.CancelAsync();
            await ramp;
            run.Dispose();
        }

        int closed = 0;
        while (_connections.TryDequeue(out RespConnection? connection))
        {
            await connection.DisposeAsync();
            closed++;
        }

        if (closed > 0)
        {
            logger.LogInformation("Reporting batch stopped; closed {Closed} Redis connections", closed);
        }

        return Status();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task RampAsync(StartBatchRequest request, CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(1 / request.ConnectionsPerSecond);
        DateTimeOffset lastWarning = DateTimeOffset.MinValue;
        try
        {
            while (_connections.Count < request.TargetConnections)
            {
                TimeSpan delay = interval;
                try
                {
                    RespConnection connection = await RespConnection.OpenAsync(endpoint, ReportingRevisions.ServiceName, ConnectTimeout, cancellationToken);
                    _connections.Enqueue(connection);
                }
                catch (RedisUnavailableException exception)
                {
                    lock (_gate)
                    {
                        _lastError = exception.InnerException?.Message ?? exception.Message;
                    }

                    // Partition workers retry a refused connection almost at once, which is what
                    // starves other clients of every slot Redis frees.
                    delay = RefusedRetryDelay;
                    if (time.GetUtcNow() - lastWarning > TimeSpan.FromSeconds(5))
                    {
                        lastWarning = time.GetUtcNow();
                        logger.LogWarning(exception, "Reporting batch could not open a Redis connection: {Reason}", _lastError);
                    }
                }

                await Task.Delay(delay, time, cancellationToken);
            }

            logger.LogInformation("Reporting batch holds {Connections} Redis connections", _connections.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopped while ramping.
        }
    }
}

public sealed class ReportingTelemetry : IDisposable
{
    public const string SourceName = "Reference.ReportingWorker";

    private readonly Meter _meter = new(SourceName);

    public ReportingTelemetry(ReportingBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        _meter.CreateObservableGauge("reference.reporting.batch.active", () => batch.Active ? 1 : 0, "{batch}", "1 while a reporting batch runs.");
        _meter.CreateObservableGauge("reference.reporting.redis.connections", () => batch.Connections, "{connection}", "Redis connections held by the batch.");
    }

    public void Dispose() => _meter.Dispose();
}
