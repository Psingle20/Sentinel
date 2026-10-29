using System.Runtime.CompilerServices;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.Live;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Live;

/// <summary>
/// The caller's live events after a sequence (or from now), then new ones as they are recorded, until
/// the caller goes away. The stream reads in batches and waits a second when it has caught up.
/// </summary>
public sealed class StreamLiveEventsQueryHandler(ILiveEventStore events) : IQueryHandler<StreamLiveEventsQuery, IAsyncEnumerable<LiveEvent>>
{
    private const int BatchSize = 100;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    public Task<Result<IAsyncEnumerable<LiveEvent>>> Handle(StreamLiveEventsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult(Result.Success(ReadAsync(query.Tenants, query.After, cancellationToken)));
    }

    private async IAsyncEnumerable<LiveEvent> ReadAsync(IReadOnlyList<TenantId> tenants, long? after, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long position = after ?? await events.LatestAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<LiveEvent> batch = await events.AfterAsync(position, tenants, BatchSize, cancellationToken);
            foreach (LiveEvent live in batch)
            {
                position = live.Sequence;
                yield return live;
            }

            if (batch.Count < BatchSize)
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }
}
