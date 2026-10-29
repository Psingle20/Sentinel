using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Sentinel.Gateway.Application.Live;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Api.Live;

/// <summary>Live events after a sequence, then new ones as they are recorded, until the client disconnects.</summary>
public static class LiveEventStream
{
    private const int BatchSize = 100;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    public static async IAsyncEnumerable<SseItem<LiveEvent>> ReadAsync(ILiveEventStore events, IReadOnlyList<TenantId> tenants, long? after, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long position = after ?? await events.LatestAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<LiveEvent> batch = await events.AfterAsync(position, tenants, BatchSize, cancellationToken);
            foreach (LiveEvent live in batch)
            {
                position = live.Sequence;
                yield return new SseItem<LiveEvent>(live, "incident") { EventId = live.Sequence.ToString(CultureInfo.InvariantCulture) };
            }

            if (batch.Count < BatchSize)
            {
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }
}
