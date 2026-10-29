using Temporalio.Common;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Sentinel.Runtime.Infrastructure.Workflows;

/// <summary>Timings fixed when a run starts, so a replay of the run sees the same values.</summary>
/// <param name="ReconciliationTimeout">
/// How long the run keeps waiting after Executor reports an unknown outcome for the conclusive result
/// of its reconciliation (ADR-006). Inputs recorded before it existed read as zero; those runs replay
/// without the wait because the patch marker guards it.
/// </param>
public sealed record WorkflowTimings(
    TimeSpan InvestigationTimeout,
    bool Challenge,
    TimeSpan ApprovalTimeout,
    TimeSpan ExecutionTimeout,
    TimeSpan RecoveryLag,
    TimeSpan RecoveryDeadline,
    TimeSpan RecoveryRetry,
    TimeSpan ReconciliationTimeout = default);
