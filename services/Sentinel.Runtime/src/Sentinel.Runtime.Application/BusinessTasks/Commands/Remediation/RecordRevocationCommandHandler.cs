using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Services;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

/// <summary>An approver withdrew an approval: the incident is handed to responders and no start can be confirmed.</summary>
public sealed class RecordRevocationCommandHandler(IncidentReportRecorder recorder) : ICommandHandler<RecordRevocationCommand>
{
    public async Task<Result> Handle(RecordRevocationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await recorder.RecordAsync(command.IncidentId, new RevokeApprovalCommand(command.PlanId, command.PlanVersion, command.RevokedBy, command.Reason), new IncidentSignal(IncidentSignalKind.Closed, command.PlanId.ToString()), "revocation", cancellationToken);
        return Result.Success();
    }
}
