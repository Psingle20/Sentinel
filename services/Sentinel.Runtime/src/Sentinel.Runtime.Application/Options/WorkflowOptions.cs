using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Options;

/// <summary>Waits and timers of the incident workflow (ADR-005).</summary>
public sealed class WorkflowOptions
{
    /// <summary>After this long without an approval or rejection the plan expires and the incident is escalated.</summary>
    public TimeSpan ApprovalTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long to wait for Executor's result before escalating; Executor reconciles unknown outcomes itself.</summary>
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>After Executor reports an outcome it could not determine, how long to wait for its reconciled result before escalating.</summary>
    public TimeSpan ReconciliationTimeout { get; set; } = TimeSpan.FromMinutes(30);

    public TimeSpan RecoveryRetryInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether a remedy that took effect at its target without recovering the incident opens a follow-up cycle (Phase E03)
    /// instead of escalating; the incident bounds it to one.
    /// </summary>
    public bool FollowUp { get; set; } = true;
}
