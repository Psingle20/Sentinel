using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Abstractions.Persistence;

/// <summary>Runtime's correlation state, built only from the observations it consumes.</summary>
public interface ICorrelationStore
{
    Task<SignalStreak?> GetStreakAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId serviceId, string metricName, CancellationToken cancellationToken);

    Task SaveStreakAsync(SignalStreak streak, CancellationToken cancellationToken);

    /// <summary>Upserts a change by deployment ID, keeping every observation ID that reported it.</summary>
    /// <remarks>An empty <c>services</c> list in <see cref="GetChangesAsync"/> means every service.</remarks>
    Task<LinkedChange> SaveChangeAsync(TenantId tenantId, EnvironmentId environmentId, LinkedChange change, CancellationToken cancellationToken);

    Task<IReadOnlyList<LinkedChange>> GetChangesAsync(TenantId tenantId, EnvironmentId environmentId, IReadOnlyCollection<ServiceId> services, DateTimeOffset startedFrom, CancellationToken cancellationToken);

    Task<LinkedChange?> GetChangeAsync(TenantId tenantId, EnvironmentId environmentId, string deploymentId, CancellationToken cancellationToken);

    /// <summary>Upserts a dependency edge observed in traces, with when it was last seen.</summary>
    Task SaveEdgeAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId caller, ServiceId callee, DateTimeOffset seenAt, CancellationToken cancellationToken);

    Task<IReadOnlyList<(ServiceId Caller, ServiceId Callee)>> GetEdgesAsync(TenantId tenantId, EnvironmentId environmentId, DateTimeOffset seenSince, CancellationToken cancellationToken);
}
