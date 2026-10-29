using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Incidents.Commands;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

/// <summary>
/// The start confirmation Executor must obtain before any side effect (ADR-006). The incident's
/// writer confirms only the current approved plan with the fencing token it was issued; refusal is
/// the safe answer and is reported, not failed.
/// </summary>
public sealed class ConfirmRemediationStartCommandHandler(IIncidentOwner owner, ILogger<ConfirmRemediationStartCommandHandler> logger) : ICommandHandler<ConfirmRemediationStartCommand, RemediationStartResponse>
{
    public async Task<Result<RemediationStartResponse>> Handle(ConfirmRemediationStartCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<CommandReply> result = await owner.SendAsync(
            command.IncidentId,
            new ConfirmStartCommand(command.TenantId, command.EnvironmentId, command.PlanId, command.PlanVersion, command.FencingToken),
            cancellationToken);

        if (result.IsFailure)
        {
            logger.LogWarning("Refused start of plan {PlanId} v{Version} for incident {IncidentId}: {Reason}", command.PlanId, command.PlanVersion, command.IncidentId, result.Error.Description);
        }

        return Result.Success(new RemediationStartResponse(result.IsSuccess, result.IsSuccess ? null : result.Error.Description));
    }
}
