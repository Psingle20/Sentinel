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

    public TimeSpan RecoveryRetryInterval { get; set; } = TimeSpan.FromSeconds(10);
}
