using Reference.Common.Redis;

namespace Reference.ReportingWorker.Batch;

/// <summary>
/// The reporting worker's batch, one at a time, started and stopped by the scenario runner. While it
/// runs, its <see cref="ReportingConnectionFanOut"/> holds Redis connections (S2). It is not a
/// deployment, so a deploy-blame heuristic cannot see it.
/// </summary>
public sealed class ReportingBatch(RedisEndpoint endpoint, TimeProvider time, ILogger<ReportingBatch> logger) : IAsyncDisposable
{
    private readonly ReportingConnectionFanOut _fanOut = new(endpoint, time, logger);
    private readonly Lock _gate = new();
    private CancellationTokenSource? _run;
    private Task _ramp = Task.CompletedTask;
    private StartBatchRequest _request = new();
    private DateTimeOffset? _startedAt;

    public int Connections => _fanOut.Connections;

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
            return new BatchStatus(_run is not null, _fanOut.Connections, _request.TargetConnections, _startedAt, _fanOut.LastError);
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
                _fanOut.ClearError();
                _run = new CancellationTokenSource();
                _ramp = _fanOut.RampAsync(request, _run.Token);
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

        int closed = await _fanOut.CloseAllAsync();
        if (closed > 0)
        {
            logger.LogInformation("Reporting batch stopped; closed {Closed} Redis connections", closed);
        }

        return Status();
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
