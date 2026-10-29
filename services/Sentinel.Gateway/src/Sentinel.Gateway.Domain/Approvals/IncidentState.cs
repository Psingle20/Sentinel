using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Domain.Approvals;

/// <summary>
/// What an approval decision depends on, as Runtime reports the incident when the approver decides:
/// whose it is, its current plan if it has one, and whether execution has started. Runtime owns the
/// incident; the Gateway only reads it (ADR-001).
/// </summary>
public sealed record IncidentState(
    TenantId TenantId,
    EnvironmentId EnvironmentId,
    IncidentId IncidentId,
    CurrentPlan? Plan,
    bool AwaitingApproval,
    bool ExecutionStarted);
