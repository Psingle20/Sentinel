using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Executor.Domain.Executions;

/// <summary>Executor's durable execution states (ADR-006).</summary>
public enum ExecutionState
{
    Received,
    Rejected,
    Reserved,
    Aborted,
    StartConfirmed,
    Executing,
    ActionVerified,
    Failed,
    OutcomeUnknown,
    Reconciling,
    Compensating,
    Compensated,
    CompensationFailed
}
