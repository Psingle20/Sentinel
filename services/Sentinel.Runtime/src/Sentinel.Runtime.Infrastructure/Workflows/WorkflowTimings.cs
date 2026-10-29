using Temporalio.Common;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Sentinel.Runtime.Infrastructure.Workflows;

/// <summary>Timings fixed when a run starts, so a replay of the run sees the same values.</summary>
public sealed record WorkflowTimings(
    TimeSpan InvestigationTimeout,
    bool Challenge,
    TimeSpan ApprovalTimeout,
    TimeSpan ExecutionTimeout,
    TimeSpan RecoveryLag,
    TimeSpan RecoveryDeadline,
    TimeSpan RecoveryRetry);
