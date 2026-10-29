using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;

namespace Sentinel.Runtime.Application;

/// <summary>
/// Runtime's inbound side of the control path (ADR-006): it records what other services report —
/// AgentWorker's findings, Api's signed approval or rejection, Executor's start confirmation and
/// result — through the incident's writer, then tells the incident's workflow so it can take the
/// next step. Runtime never creates or alters an approval; the workflow, not this service, asks
/// Executor to act.
/// </summary>
public sealed class RemediationControlService(IIncidentOwner owner, IIncidentWorkflows workflows, ILogger<RemediationControlService> logger)
{
    public async Task<Result> HandleInvestigationAsync(InvestigationCompleted completed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completed);

        // A report the incident cannot take (unknown investigation, wrong state) is logged and dropped, never retried.
        await RecordAsync(completed.IncidentId, new RecordInvestigationCommand(completed.ToReport()), new IncidentSignal(IncidentSignalKind.InvestigationReported, completed.InvestigationId.ToString()), "investigation report", cancellationToken);
        return Result.Success();
    }

    public async Task<Result> HandleApprovalAsync(RecordApproval approval, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approval);

        await RecordAsync(
            approval.IncidentId,
            new ApprovePlanCommand(approval.PlanId, approval.PlanVersion, approval.PlanHash, approval.ApproverId, approval.Attestation),
            new IncidentSignal(IncidentSignalKind.ApprovalRecorded, approval.PlanId.ToString()),
            "approval",
            cancellationToken);
        return Result.Success();
    }

    public async Task<Result> HandleRejectionAsync(RecordRejection rejection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rejection);

        await RecordAsync(
            rejection.IncidentId,
            new RejectPlanCommand(rejection.PlanId, rejection.PlanVersion, rejection.RejectedBy, rejection.Reason),
            new IncidentSignal(IncidentSignalKind.PlanRejected, rejection.PlanId.ToString()),
            "rejection",
            cancellationToken);
        return Result.Success();
    }

    /// <summary>An approver withdrew an approval: the incident is handed to responders and no start can be confirmed.</summary>
    public async Task<Result> HandleRevocationAsync(RecordRevocation revocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(revocation);

        await RecordAsync(
            revocation.IncidentId,
            new RevokeApprovalCommand(revocation.PlanId, revocation.PlanVersion, revocation.RevokedBy, revocation.Reason),
            new IncidentSignal(IncidentSignalKind.Closed, revocation.PlanId.ToString()),
            "revocation",
            cancellationToken);
        return Result.Success();
    }

    /// <summary>The start confirmation Executor must obtain before any side effect. Refusal is the safe answer.</summary>
    public async Task<RemediationStartResponse> ConfirmStartAsync(RemediationStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<CommandReply> result = await owner.SendAsync(
            request.IncidentId,
            new ConfirmStartCommand(request.TenantId, request.EnvironmentId, request.PlanId, request.PlanVersion, request.FencingToken),
            cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Refused start of plan {PlanId} v{Version} for incident {IncidentId}: {Reason}", request.PlanId, request.PlanVersion, request.IncidentId, result.Error.Description);
        }

        return new RemediationStartResponse(result.IsSuccess, result.IsSuccess ? null : result.Error.Description);
    }

    public async Task<Result> HandleExecutedAsync(RemediationExecuted executed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executed);

        await RecordAsync(
            executed.IncidentId,
            new RecordExecutionCommand(executed.PlanId, executed.PlanVersion, executed.Outcome.ToDomain(), executed.Detail),
            new IncidentSignal(IncidentSignalKind.ExecutionReported, executed.Outcome.ToString()),
            "execution result",
            cancellationToken);
        return Result.Success();
    }

    private async Task RecordAsync(IncidentId incidentId, IncidentCommand command, IncidentSignal signal, string what, CancellationToken cancellationToken)
    {
        Result<CommandReply> result = await owner.SendAsync(incidentId, command, cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning("Incident {IncidentId} refused the {What}: {Reason}", incidentId, what, result.Error.Description);
            return;
        }

        // Signalled even when nothing changed: a redelivered message re-sends a signal that may have been lost.
        await workflows.SignalAsync(result.Value.Snapshot, signal, cancellationToken);
    }
}

/// <summary>
/// Rebuilds every incident's read model from its stream (Phase 03 exit criterion: projections can be
/// deleted and rebuilt). It overwrites each projection in place, so it never needs to delete anything
/// and can run while Runtime serves traffic.
/// </summary>
public sealed class ProjectionRebuilder(IIncidentStore incidents, CleanArchitecture.BuildingBlocks.Integration.IUnitOfWork unitOfWork, ILogger<ProjectionRebuilder> logger)
{
    public async Task<int> RebuildAsync(CancellationToken cancellationToken)
    {
        int rebuilt = 0;
        foreach (IncidentId incidentId in await incidents.AllIncidentIdsAsync(cancellationToken))
        {
            IReadOnlyList<StoredEvent> history = await incidents.ReadStreamAsync(incidentId, cancellationToken);
            if (history.Count == 0)
            {
                continue;
            }

            Sentinel.Contracts.Incidents.IncidentDetail detail = IncidentProjector.Project(history);
            await incidents.SaveProjectionAsync(detail, detail.Summary.Status != Sentinel.Contracts.Incidents.IncidentStatus.Resolved, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            rebuilt++;
        }

        logger.LogInformation("Rebuilt {Count} incident projection(s) from their streams", rebuilt);
        return rebuilt;
    }
}
