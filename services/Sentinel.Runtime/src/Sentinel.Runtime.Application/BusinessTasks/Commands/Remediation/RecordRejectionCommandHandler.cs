using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Incidents.Commands;
using Sentinel.Runtime.Application.Services;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

/// <summary>
/// Verifies the rejection's attestation, then records the rejection through the incident's single writer and signals the
/// incident's workflow. A rejection whose attestation does not verify, including one without any, is dropped (ADR-006).
/// </summary>
public sealed class RecordRejectionCommandHandler(IncidentReportRecorder recorder, DecisionVerifier verifier, ILogger<RecordRejectionCommandHandler> logger) : ICommandHandler<RecordRejectionCommand>
{
    public async Task<Result> Handle(RecordRejectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await verifier.DecisionProblemAsync(command.Attestation, ApprovalAttestation.RejectPlanScope, command.IncidentId, command.PlanId, command.PlanVersion, command.RejectedBy, cancellationToken) is { } problem)
        {
            logger.LogWarning("Refused the rejection of plan {PlanId} v{Version} for incident {IncidentId}: {Reason}", command.PlanId, command.PlanVersion, command.IncidentId, problem);
            return Result.Success();
        }

        await recorder.RecordAsync(command.IncidentId, new RejectPlanCommand(command.PlanId, command.PlanVersion, command.RejectedBy, command.Reason), new IncidentSignal(IncidentSignalKind.PlanRejected, command.PlanId.ToString()), "rejection", cancellationToken);
        return Result.Success();
    }
}
