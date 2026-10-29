using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Services;
using Sentinel.Runtime.Application.Abstractions.Runtime;

namespace Sentinel.Runtime.Application;

/// <summary>
/// The rest of Runtime's inbound control path (ADR-006): AgentWorker's findings, Executor's start
/// confirmation and its result. Approvals, rejections and revocations are requests already; these
/// follow in Runtime's own refactoring step. The workflow, not this service, asks Executor to act.
/// </summary>
public sealed class RemediationControlService(IIncidentOwner owner, IncidentReportRecorder recorder, ILogger<RemediationControlService> logger)
{
    public async Task<Result> HandleInvestigationAsync(InvestigationCompleted completed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completed);

        await recorder.RecordAsync(completed.IncidentId, new RecordInvestigationCommand(completed.ToReport()), new IncidentSignal(IncidentSignalKind.InvestigationReported, completed.InvestigationId.ToString()), "investigation report", cancellationToken);
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

        await recorder.RecordAsync(
            executed.IncidentId,
            new RecordExecutionCommand(executed.PlanId, executed.PlanVersion, executed.Outcome.ToDomain(), executed.Detail),
            new IncidentSignal(IncidentSignalKind.ExecutionReported, executed.Outcome.ToString()),
            "execution result",
            cancellationToken);
        return Result.Success();
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
