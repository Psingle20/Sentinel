using System.Globalization;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.Live;

namespace Sentinel.Gateway.Application.Abstractions.Persistence;

/// <summary>Api's resumable live-event log: appended in the inbox transaction, read by sequence.</summary>
public interface ILiveEventStore
{
    Task AppendAsync(LiveEventDraft draft, CancellationToken cancellationToken);

    /// <summary>Events after <paramref name="after"/> in the given tenants, oldest first.</summary>
    Task<IReadOnlyList<LiveEvent>> AfterAsync(long after, IReadOnlyCollection<TenantId> tenants, int limit, CancellationToken cancellationToken);

    /// <summary>The newest sequence, where a client that asks for no history starts.</summary>
    Task<long> LatestAsync(CancellationToken cancellationToken);
}
