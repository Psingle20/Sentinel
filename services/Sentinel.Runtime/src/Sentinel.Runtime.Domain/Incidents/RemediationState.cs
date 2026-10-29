namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>Where the incident's remediation stands, from no plan to a reported outcome.</summary>
public enum RemediationState
{
    NoPlan,
    Proposed,
    Approved,
    Rejected,
    Revoked,
    ExecutionRequested,
    Executing,
    OutcomeUnknown,
    Finished
}
