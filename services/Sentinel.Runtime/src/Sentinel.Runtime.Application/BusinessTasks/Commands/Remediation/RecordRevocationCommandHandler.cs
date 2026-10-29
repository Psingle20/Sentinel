using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Incidents.Commands;
using Sentinel.Runtime.Application.Services;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

/// <summary>
/// An approver withdraws an approval (ADR-006). The attestation must verify, and the incident's writer decides: refused
/// once execution started, otherwise accepted, after which the incident is handed to responders and no start can be
/// confirmed. The answer is final either way, so the Gateway records a revocation only when it was accepted.
/// </summary>
public sealed class RecordRevocationCommandHandler(
    IIncidentOwner owner,
    IIncidentWorkflows workflows,
    DecisionVerifier verifier,
    ILogger<RecordRevocationCommandHandler> logger) : ICommandHandler<RecordRevocationCommand, RevocationResponse>
{
    public async Task<Result<RevocationResponse>> Handle(RecordRevocationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (await verifier.DecisionProblemAsync(command.Attestation, ApprovalAttestation.RevokeApprovalScope, command.IncidentId, command.PlanId, command.PlanVersion, command.RevokedBy, cancellationToken) is { } problem)
        {
            logger.LogWarning("Refused the revocation of plan {PlanId} v{Version} for incident {IncidentId}: {Reason}", command.PlanId, command.PlanVersion, command.IncidentId, problem);
            return Result.Success(new RevocationResponse(false, problem));
        }

        Result<CommandReply> result = await owner.SendAsync(command.IncidentId, new RevokeApprovalCommand(command.PlanId, command.PlanVersion, command.RevokedBy, command.Reason), cancellationToken);
        if (result.IsFailure)
        {
            logger.LogWarning("Incident {IncidentId} refused the revocation: {Reason}", command.IncidentId, result.Error.Description);
            return Result.Success(new RevocationResponse(false, result.Error.Description));
        }

        try
        {
            await workflows.SignalAsync(result.Value.Snapshot, new IncidentSignal(IncidentSignalKind.Closed, command.PlanId.ToString()), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The stream accepted the revocation, so a lost signal must not turn that answer into a failure.
            logger.LogWarning(exception, "Incident {IncidentId} accepted the revocation, but its workflow could not be signalled", command.IncidentId);
        }

        return Result.Success(new RevocationResponse(true, null));
    }
}
