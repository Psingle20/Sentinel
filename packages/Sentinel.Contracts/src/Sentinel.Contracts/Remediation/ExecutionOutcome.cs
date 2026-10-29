using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>Terminal states of Executor's execution state machine (ADR-006).</summary>
public enum ExecutionOutcome
{
    Rejected,
    Aborted,
    ActionVerified,
    Failed,
    OutcomeUnknown,
    Compensated,
    CompensationFailed
}
