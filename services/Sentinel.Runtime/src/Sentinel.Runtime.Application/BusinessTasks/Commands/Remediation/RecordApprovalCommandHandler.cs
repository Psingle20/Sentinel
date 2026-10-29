using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Incidents.Commands;
using Sentinel.Runtime.Application.Services;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

/// <summary>
/// Verifies the approval's attestation, then records the approval through the incident's single writer, which checks that
/// it is for the current plan, and signals the incident's workflow, which asks Executor to act. Runtime relays the
/// attestation; it never creates or alters one, and an approval whose attestation does not verify is dropped (ADR-006).
/// </summary>
public sealed class RecordApprovalCommandHandler(IncidentReportRecorder recorder, DecisionVerifier verifier, ILogger<RecordApprovalCommandHandler> logger) : ICommandHandler<RecordApprovalCommand>
{
    public async Task<Result> Handle(RecordApprovalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await verifier.ApprovalProblemAsync(command.Attestation, command.IncidentId, command.PlanId, command.PlanVersion, command.PlanHash, command.ApproverId, cancellationToken) is { } problem)
        {
            logger.LogWarning("Refused the approval of plan {PlanId} v{Version} for incident {IncidentId}: {Reason}", command.PlanId, command.PlanVersion, command.IncidentId, problem);
            return Result.Success();
        }

        await recorder.RecordAsync(command.IncidentId, new ApprovePlanCommand(command.PlanId, command.PlanVersion, command.PlanHash, command.ApproverId, command.Attestation), new IncidentSignal(IncidentSignalKind.ApprovalRecorded, command.PlanId.ToString()), "approval", cancellationToken);
        return Result.Success();
    }
}
