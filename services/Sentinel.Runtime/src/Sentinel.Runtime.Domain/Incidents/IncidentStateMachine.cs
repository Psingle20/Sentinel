namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// The incident lifecycle as tables (ADR-004): which state each state may move to, and in which
/// states each operation may change the incident. <see cref="Incident"/> checks these tables and
/// never compares its state inline.
/// </summary>
public static class IncidentStateMachine
{
    private static readonly IncidentState[] Open =
        [IncidentState.Detected, IncidentState.Investigating, IncidentState.AwaitingApproval, IncidentState.Remediating, IncidentState.Verifying, IncidentState.Escalated];

    private static readonly Dictionary<IncidentState, IncidentState[]> Transitions = new()
    {
        [IncidentState.Detected] = [IncidentState.Investigating, IncidentState.Escalated],
        [IncidentState.Investigating] = [IncidentState.Investigating, IncidentState.AwaitingApproval, IncidentState.Escalated],
        [IncidentState.AwaitingApproval] = [IncidentState.Investigating, IncidentState.AwaitingApproval, IncidentState.Remediating, IncidentState.Escalated, IncidentState.Resolved],
        [IncidentState.Remediating] = [IncidentState.Verifying, IncidentState.Escalated],
        [IncidentState.Verifying] = [IncidentState.Detected, IncidentState.Resolved, IncidentState.Escalated],
        [IncidentState.Escalated] = [IncidentState.Investigating, IncidentState.Resolved],
        [IncidentState.Resolved] = [IncidentState.Detected]
    };

    private static readonly Dictionary<IncidentOperation, IncidentState[]> Operations = new()
    {
        [IncidentOperation.RequestInvestigation] = [IncidentState.Detected, IncidentState.Investigating],
        [IncidentOperation.RecordDecision] = [IncidentState.Investigating],
        [IncidentOperation.SupersedeDecision] = [IncidentState.Investigating, IncidentState.AwaitingApproval, IncidentState.Escalated],
        [IncidentOperation.EscalateWithoutPlan] = [IncidentState.Investigating],
        [IncidentOperation.Escalate] = [IncidentState.Detected, IncidentState.Investigating, IncidentState.AwaitingApproval, IncidentState.Remediating, IncidentState.Verifying],
        [IncidentOperation.LinkSignal] = Open,
        [IncidentOperation.ChangeSeverity] = Open,
        [IncidentOperation.Reopen] = [IncidentState.Resolved],
        [IncidentOperation.Approve] = [IncidentState.AwaitingApproval],
        [IncidentOperation.Reject] = [IncidentState.AwaitingApproval],
        [IncidentOperation.RequestExecution] = [IncidentState.AwaitingApproval],
        [IncidentOperation.ConfirmStart] = [IncidentState.Remediating],
        [IncidentOperation.RecordRecovery] = [IncidentState.Verifying],
        [IncidentOperation.ResolveCleared] = [IncidentState.AwaitingApproval, IncidentState.Escalated]
    };

    /// <summary>Whether <paramref name="operation"/> may change an incident in <paramref name="state"/>.</summary>
    public static bool Allows(IncidentState state, IncidentOperation operation) => Operations[operation].Contains(state);

    /// <summary>Whether an incident in <paramref name="from"/> may move to <paramref name="to"/>.</summary>
    public static bool CanMove(IncidentState from, IncidentState to) => from == to || Transitions[from].Contains(to);

    public static bool IsOpen(IncidentState state) => Open.Contains(state);
}
