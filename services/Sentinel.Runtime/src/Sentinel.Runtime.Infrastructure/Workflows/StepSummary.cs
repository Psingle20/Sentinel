using Temporalio.Common;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Sentinel.Runtime.Infrastructure.Workflows;

/// <summary>The workflow's view of an incident: identifiers and states only (ADR-005 payload rule).</summary>
public sealed record StepSummary(
    string State,
    string? Decision,
    bool Approved,
    bool Rejected,
    string? Execution,
    DateTime? RecoveryWindowTo,
    IReadOnlyList<Guid> PendingInvestigationIds,
    IReadOnlyList<Guid> ReportedInvestigationIds,
    Guid? EscalationId = null)
{
    public bool IsOpen => State != "Resolved";
}
