using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Executor.Domain.Executions;

/// <summary>The persisted form of an <see cref="ExecutionRequest"/>.</summary>
public sealed record ExecutionSnapshot(
    Guid RequestId,
    string IdempotencyKey,
    TenantId TenantId,
    EnvironmentId EnvironmentId,
    IncidentId IncidentId,
    PlanId PlanId,
    int PlanVersion,
    string PlanHash,
    long FencingToken,
    string Plan,
    AttestationId? AttestationId,
    string? ApproverId,
    string? PolicyVersion,
    ExecutionState State,
    string? Detail,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ExecutionTransition> History,
    IReadOnlyList<ActionId>? CompletedActions = null);
