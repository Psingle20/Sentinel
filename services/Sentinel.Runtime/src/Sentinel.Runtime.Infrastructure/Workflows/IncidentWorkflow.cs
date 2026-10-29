using Temporalio.Common;
using Temporalio.Exceptions;
using Temporalio.Workflows;

namespace Sentinel.Runtime.Infrastructure.Workflows;

/// <summary>
/// The incident's durable workflow (ADR-005), one run per incident cycle: investigate, challenge,
/// decide, plan, wait for approval, execute and verify recovery. It holds step progress only. It
/// performs no I/O, never reads the clock or calls grains: every read and change goes through an
/// activity, and the incident stream stays the truth (ADR-004). Waits are signals with timeouts;
/// the recovery window is a durable timer, so a Runtime restart resumes exactly where it stopped.
/// </summary>
[Workflow(Name)]
public sealed class IncidentWorkflow
{
    public const string Name = "sentinel.incident";
    public const string TaskQueue = "sentinel-runtime";

    /// <summary>Versioning marker (ADR-005): runs started before it skip the re-read before the approval wait.</summary>
    public const string RereadBeforeApprovalPatch = "reread-before-approval-wait";

    /// <summary>Versioning marker (ADR-005): runs that reached the execution result before it escalate an unknown outcome at once.</summary>
    public const string AwaitReconciliationPatch = "await-reconciliation-after-unknown-outcome";

    private static readonly ActivityOptions Step = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(1),
        ScheduleToCloseTimeout = TimeSpan.FromMinutes(30),
        RetryPolicy = new RetryPolicy
        {
            InitialInterval = TimeSpan.FromSeconds(1),
            BackoffCoefficient = 2,
            MaximumInterval = TimeSpan.FromSeconds(30)
        }
    };

    private readonly HashSet<Guid> _reported = [];
    private bool _approval;
    private bool _rejected;
    private bool _executed;
    private int _executionReports;
    private bool _closed;

    /// <summary>The step the run is at, for operators and the workflow tests.</summary>
    [WorkflowQuery]
    public string CurrentStep { get; private set; } = "starting";

    [WorkflowRun]
    public async Task<string> RunAsync(IncidentWorkflowInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            return await RunCycleAsync(input);
        }
        catch (Exception exception) when (TemporalException.IsCanceledException(exception))
        {
            // Compensation: a cancelled run hands the incident to humans, which also makes Runtime
            // refuse any start confirmation for an execution that was requested but not started.
            using var detached = new CancellationTokenSource();
            await Workflow.ExecuteActivityAsync(
                (IncidentActivities activities) => activities.EscalateAsync(input.IncidentId, "The incident workflow was cancelled; responders take over."),
                new ActivityOptions { StartToCloseTimeout = TimeSpan.FromMinutes(1), CancellationToken = detached.Token });
            throw;
        }
    }

    [WorkflowSignal]
    public Task SignalAsync(IncidentWorkflowSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        switch (signal.Kind)
        {
            case "InvestigationReported" when Guid.TryParse(signal.Reference, out Guid investigationId):
                _reported.Add(investigationId);
                break;
            case "ApprovalRecorded":
                _approval = true;
                break;
            case "PlanRejected":
                _rejected = true;
                break;
            case "ExecutionReported":
                _executed = true;
                _executionReports++;
                break;
            case "Closed":
                _closed = true;
                break;
        }

        return Task.CompletedTask;
    }

    private async Task<string> RunCycleAsync(IncidentWorkflowInput input)
    {
        Guid incidentId = input.IncidentId;
        WorkflowTimings timings = input.Timings;

        CurrentStep = "investigating";
        IReadOnlyList<Guid> investigations = await Workflow.ExecuteActivityAsync((IncidentActivities activities) => activities.RequestInvestigationsAsync(incidentId), Step);
        await WaitForReportsAsync(incidentId, investigations, timings.InvestigationTimeout);
        if (_closed)
        {
            return "closed";
        }

        if (timings.Challenge)
        {
            CurrentStep = "challenging";
            Guid? challenge = await Workflow.ExecuteActivityAsync((IncidentActivities activities) => activities.RequestChallengeAsync(incidentId), Step);
            if (challenge is { } challengeId)
            {
                await WaitForReportsAsync(incidentId, [challengeId], timings.InvestigationTimeout);
            }
        }

        CurrentStep = "deciding";
        StepSummary decided = await Workflow.ExecuteActivityAsync((IncidentActivities activities) => activities.DecideAsync(incidentId), Step);
        if (decided.Decision != "Diagnosed")
        {
            return "abstained";
        }

        CurrentStep = "planning";
        StepSummary planned = await Workflow.ExecuteActivityAsync((IncidentActivities activities) => activities.PlanAsync(incidentId), Step);
        if (planned.State != "AwaitingApproval")
        {
            return "escalated-without-plan";
        }

        CurrentStep = "awaiting-approval";
        if (Workflow.Patched(RereadBeforeApprovalPatch))
        {
            // An approval recorded before this run began waiting is already in the stream.
            StepSummary current = await GetAsync(incidentId);
            _approval |= current.Approved;
            _rejected |= current.Rejected;
            _closed |= !current.IsOpen;
        }

        bool answered = await Workflow.WaitConditionAsync(() => _approval || _rejected || _closed, timings.ApprovalTimeout);
        if (!answered)
        {
            await Workflow.ExecuteActivityAsync((IncidentActivities activities) => activities.EscalateAsync(incidentId, "The approval window expired with no decision; responders take over."), Step);
            return "approval-expired";
        }

        // The stream wins: a signal is only a hint to look.
        StepSummary answer = await GetAsync(incidentId);
        if (!answer.Approved || answer.State != "AwaitingApproval")
        {
            return answer.Rejected ? "rejected" : "closed";
        }

        CurrentStep = "executing";
        await Workflow.ExecuteActivityAsync((IncidentActivities activities) => activities.RequestExecutionAsync(incidentId), Step);
        await Workflow.WaitConditionAsync(() => _executed || _closed, timings.ExecutionTimeout);
        int reportsRead = _executionReports;
        StepSummary executed = await GetAsync(incidentId);
        if (executed.Execution is "OutcomeUnknown" && Workflow.Patched(AwaitReconciliationPatch))
        {
            executed = await AwaitReconciliationAsync(incidentId, executed, reportsRead, timings.ReconciliationTimeout);
        }

        if (executed.Execution is null or "OutcomeUnknown")
        {
            await Workflow.ExecuteActivityAsync((IncidentActivities activities) => activities.EscalateAsync(incidentId, "Executor reported no conclusive result in time; responders must check the action."), Step);
            return "execution-timeout";
        }

        if (executed.Execution != "ActionVerified")
        {
            return "execution-failed";
        }

        CurrentStep = "verifying-recovery";
        StepSummary scheduled = await Workflow.ExecuteActivityAsync((IncidentActivities activities) => activities.ScheduleRecoveryAsync(incidentId), Step);
        DateTime judgeAt = (scheduled.RecoveryWindowTo ?? Workflow.UtcNow) + timings.RecoveryLag;
        if (judgeAt > Workflow.UtcNow)
        {
            await Workflow.DelayAsync(judgeAt - Workflow.UtcNow);
        }

        DateTime deadline = judgeAt + timings.RecoveryDeadline;
        while (true)
        {
            bool final = Workflow.UtcNow >= deadline;
            string result = await Workflow.ExecuteActivityAsync((IncidentActivities activities) => activities.VerifyRecoveryAsync(incidentId, final), Step);
            if (result != "Retry")
            {
                break;
            }

            await Workflow.DelayAsync(timings.RecoveryRetry);
        }

        CurrentStep = "done";
        return "recovery-judged";
    }

    /// <summary>
    /// Executor keeps reconciling an outcome it could not determine and reports again once it can, so an
    /// unknown outcome is waited on, not escalated, until the reconciliation window closes. Each report is
    /// re-read from the stream; one that arrived after <paramref name="reportsRead"/> ends the wait at once.
    /// </summary>
    private async Task<StepSummary> AwaitReconciliationAsync(Guid incidentId, StepSummary current, int reportsRead, TimeSpan window)
    {
        CurrentStep = "awaiting-reconciliation";
        DateTime until = Workflow.UtcNow + window;
        while (current.Execution is "OutcomeUnknown" && !_closed && Workflow.UtcNow < until)
        {
            int seen = reportsRead;
            await Workflow.WaitConditionAsync(() => _executionReports > seen || _closed, until - Workflow.UtcNow);
            reportsRead = _executionReports;
            current = await GetAsync(incidentId);
        }

        return current;
    }

    /// <summary>Waits for every investigation to report; reports already in the stream count, and stragglers are timed out.</summary>
    private async Task WaitForReportsAsync(Guid incidentId, IReadOnlyList<Guid> investigationIds, TimeSpan timeout)
    {
        if (investigationIds.Count == 0)
        {
            return;
        }

        StepSummary current = await GetAsync(incidentId);
        _reported.UnionWith(current.ReportedInvestigationIds);
        _closed |= !current.IsOpen;

        bool all = await Workflow.WaitConditionAsync(() => _closed || investigationIds.All(_reported.Contains), timeout);
        if (!all && !_closed)
        {
            Guid[] missing = [.. investigationIds.Where(id => !_reported.Contains(id))];
            await Workflow.ExecuteActivityAsync((IncidentActivities activities) => activities.TimeOutInvestigationsAsync(incidentId, missing), Step);
        }
    }

    private static Task<StepSummary> GetAsync(Guid incidentId) =>
        Workflow.ExecuteActivityAsync((IncidentActivities activities) => activities.GetAsync(incidentId), Step);
}
