using System.Collections.Concurrent;
using Reference.Common.Redis;
using Reference.ReportingWorker.Revisions;

namespace Reference.ReportingWorker.Batch;

/// <summary>
/// The S2 fault: the batch opens one Redis connection per report partition, at the requested rate,
/// until it holds its target, and keeps them until it stops. A refused connection is retried almost at
/// once, which starves other clients of every slot Redis frees.
/// </summary>
public sealed class ReportingConnectionFanOut(RedisEndpoint endpoint, TimeProvider time, ILogger logger)
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan RefusedRetryDelay = TimeSpan.FromMilliseconds(20);

    private readonly ConcurrentQueue<RespConnection> _connections = new();
    private string? _lastError;

    public int Connections => _connections.Count;

    public string? LastError => Volatile.Read(ref _lastError);

    public void ClearError() => Volatile.Write(ref _lastError, null);

    public async Task RampAsync(StartBatchRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

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
                    string error = exception.InnerException?.Message ?? exception.Message;
                    Volatile.Write(ref _lastError, error);

                    // Partition workers retry a refused connection almost at once, which is what
                    // starves other clients of every slot Redis frees.
                    delay = RefusedRetryDelay;
                    if (time.GetUtcNow() - lastWarning > TimeSpan.FromSeconds(5))
                    {
                        lastWarning = time.GetUtcNow();
                        logger.LogWarning(exception, "Reporting batch could not open a Redis connection: {Reason}", error);
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

    /// <summary>Closes every connection the batch holds and returns how many there were.</summary>
    public async Task<int> CloseAllAsync()
    {
        int closed = 0;
        while (_connections.TryDequeue(out RespConnection? connection))
        {
            await connection.DisposeAsync();
            closed++;
        }

        return closed;
    }
}
