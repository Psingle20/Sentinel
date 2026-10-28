using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application;

public sealed record StoredEvent(long Version, IIncidentEvent Event);

/// <summary>Raised when another writer appended to the stream first; the operation is retried from a fresh read.</summary>
public sealed class IncidentConcurrencyException(IncidentId incidentId, long expectedVersion)
    : Exception($"Incident {incidentId} moved past version {expectedVersion} while this change was being made.");

/// <summary>
/// The incident event store and its read model. Appends are optimistic on the expected version;
/// the read model is a projection, rebuildable from the stream (ADR-004).
/// </summary>
public interface IIncidentStore
{
    Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(IncidentId incidentId, CancellationToken cancellationToken);

    Task AppendAsync(Incident incident, long expectedVersion, IReadOnlyList<StoredEvent> events, CancellationToken cancellationToken);

    Task SaveProjectionAsync(IncidentDetail detail, bool isOpen, CancellationToken cancellationToken);

    Task<IncidentId?> FindOpenAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId serviceId, CancellationToken cancellationToken);

    /// <summary>Open incidents and those resolved since <paramref name="resolvedSince"/>, for correlation.</summary>
    Task<IReadOnlyList<IncidentContext>> CorrelationCandidatesAsync(TenantId tenantId, EnvironmentId environmentId, DateTimeOffset resolvedSince, CancellationToken cancellationToken);

    /// <summary>The stream version each projection was built from, to report projection lag.</summary>
    Task<long?> ProjectionVersionAsync(IncidentId incidentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<IncidentSummary>> ListAsync(int limit, CancellationToken cancellationToken);

    Task<IncidentDetail?> GetDetailAsync(IncidentId incidentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<IncidentId>> AllIncidentIdsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Incident memory (Phase 07): one record per closed incident and the feature index similar-incident
/// retrieval reads. The index is derived; deleting a record removes it from the index too.
/// </summary>
public interface IIncidentMemoryStore
{
    /// <summary>Inserts or replaces the record and its features, unless the record was deleted.</summary>
    Task SaveAsync(IncidentMemoryView memory, CancellationToken cancellationToken);

    Task<IncidentMemoryView?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken);

    /// <summary>
    /// Records of the tenant sharing at least one of <paramref name="features"/>, in
    /// <paramref name="environmentId"/> unless it is null, retained at <paramref name="now"/> or on
    /// legal hold, and not deleted. Never returns another tenant's memory.
    /// </summary>
    Task<IReadOnlyList<IncidentMemoryView>> CandidatesAsync(TenantId tenantId, EnvironmentId? environmentId, IReadOnlyCollection<string> features, DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>Records past their retention, not on legal hold and not yet deleted.</summary>
    Task<IReadOnlyList<IncidentMemoryView>> ExpiredAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>Replaces a record with its tombstone and removes it from every derived index.</summary>
    Task DeleteAsync(IncidentMemoryView tombstone, CancellationToken cancellationToken);
}

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

/// <summary>
/// The incident's single writer (ADR-004): IncidentGrain in a running Runtime, an in-process lock in
/// tests. It serializes every command for one incident and appends with optimistic concurrency.
/// </summary>
public interface IIncidentOwner
{
    Task<Result<CommandReply>> SendAsync(IncidentId incidentId, IncidentCommand command, CancellationToken cancellationToken);

    /// <summary>The authoritative state, read from the stream rather than the projection.</summary>
    Task<IncidentSnapshot?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken);
}

/// <summary>
/// Serializes correlation for one tenant and environment (CorrelationGrain in a running Runtime),
/// so two signals for the same failure can never open two incidents.
/// </summary>
public interface ICorrelationPartitions
{
    Task ObserveAsync(Observation observation, CancellationToken cancellationToken);
}

public enum IncidentSignalKind
{
    InvestigationReported,
    ApprovalRecorded,
    PlanRejected,
    ExecutionReported,
    Closed
}

/// <summary>A hint to the incident's workflow that the stream changed; the workflow re-reads the incident to act on it.</summary>
public sealed record IncidentSignal(IncidentSignalKind Kind, string? Reference = null);

/// <summary>The durable incident workflow (ADR-005), one run per incident cycle.</summary>
public interface IIncidentWorkflows
{
    /// <summary>Starts the cycle's workflow unless it is already running or finished; safe to repeat.</summary>
    Task EnsureStartedAsync(IncidentSnapshot incident, CancellationToken cancellationToken);

    Task SignalAsync(IncidentSnapshot incident, IncidentSignal signal, CancellationToken cancellationToken);
}

/// <summary>Hot context for one service (ServiceGrain): catalog facts and observed dependencies. A cache, never authoritative.</summary>
public sealed record ServiceContext(ServiceId ServiceId, string? Owner, IReadOnlyList<ServiceId> Dependencies, IReadOnlyList<ServiceId> Dependents);

public interface IServiceCatalog
{
    Task<ServiceContext> GetAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId serviceId, CancellationToken cancellationToken);

    /// <summary>Records a dependency edge observed in traces.</summary>
    Task ObserveEdgeAsync(TenantId tenantId, EnvironmentId environmentId, ServiceId caller, ServiceId callee, CancellationToken cancellationToken);
}

/// <summary>Executor's advisory plan-time policy evaluation (ADR-006).</summary>
public interface IPolicyEvaluator
{
    Task<PolicyEvaluation> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken);
}

/// <summary>Ingestion's observation query contract (ADR-007).</summary>
public interface IObservationQueries
{
    Task<ObservationQueryResult> QueryAsync(ObservationQuery query, CancellationToken cancellationToken);
}
