using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Options;
using Sentinel.Ingestion.Application.Options;

namespace Sentinel.Ingestion.Application.BusinessTasks.Queries;

/// <summary>
/// The bulkhead between query serving and streaming ingestion (Phase 02). At most
/// <see cref="IngestionOptions.QueryConcurrency"/> queries run at once and at most
/// <see cref="IngestionOptions.QueryQueueLimit"/> wait; anything beyond is refused immediately with
/// <see cref="QueryErrors.BulkheadFull"/>, so a burst of investigation queries can neither exhaust the
/// database pool the ingestion worker needs nor grow memory without bound. Observation queries and
/// knowledge searches share it.
/// </summary>
public sealed class QueryBulkhead : IDisposable
{
    private readonly IngestionOptions _options;
    private readonly SemaphoreSlim _slots;
    private int _admitted;

    public QueryBulkhead(IOptions<IngestionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _slots = new SemaphoreSlim(_options.QueryConcurrency, _options.QueryConcurrency);
    }

    /// <summary>Queries running or waiting now.</summary>
    public int InFlight => Volatile.Read(ref _admitted);

    public async Task<Result<TValue>> RunAsync<TValue>(Func<CancellationToken, Task<Result<TValue>>> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (Interlocked.Increment(ref _admitted) > _options.QueryConcurrency + _options.QueryQueueLimit)
        {
            Interlocked.Decrement(ref _admitted);
            return Result.Failure<TValue>(QueryErrors.BulkheadFull);
        }

        try
        {
            if (!await _slots.WaitAsync(_options.QueryQueueTimeout, cancellationToken))
            {
                return Result.Failure<TValue>(QueryErrors.BulkheadFull);
            }

            try
            {
                return await query(cancellationToken);
            }
            finally
            {
                _slots.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _admitted);
        }
    }

    public void Dispose() => _slots.Dispose();
}
