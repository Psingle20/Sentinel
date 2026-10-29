using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Services;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

/// <summary>Records the rejection through the incident's single writer and signals the incident's workflow.</summary>
public sealed class RecordRejectionCommandHandler(IncidentReportRecorder recorder) : ICommandHandler<RecordRejectionCommand>
{
    public async Task<Result> Handle(RecordRejectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await recorder.RecordAsync(command.IncidentId, new RejectPlanCommand(command.PlanId, command.PlanVersion, command.RejectedBy, command.Reason), new IncidentSignal(IncidentSignalKind.PlanRejected, command.PlanId.ToString()), "rejection", cancellationToken);
        return Result.Success();
    }
}