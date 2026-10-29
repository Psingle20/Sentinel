using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;

namespace Sentinel.Gateway.Application.Abstractions.Persistence;

/// <summary>Api's audit history of control-plane changes (Phase 08): who changed what, when, with what result.</summary>
public interface IAuditLog
{
    Task RecordAsync(string actor, string action, string target, object detail, DateTimeOffset occurredAt, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEntry>> RecentAsync(int limit, CancellationToken cancellationToken);
}
