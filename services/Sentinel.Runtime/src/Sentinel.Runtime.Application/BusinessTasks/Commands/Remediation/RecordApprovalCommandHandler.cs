using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Services;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

/// <summary>Records the approval through the incident's single writer, which checks that it is for the current plan, then signals the incident's workflow, which asks Executor to act. Runtime relays the attestation; it never creates or alters one.</summary>
public sealed class RecordApprovalCommandHandler(IncidentReportRecorder recorder) : ICommandHandler<RecordApprovalCommand>
{
    public async Task<Result> Handle(RecordApprovalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await recorder.RecordAsync(command.IncidentId, new ApprovePlanCommand(command.PlanId, command.PlanVersion, command.PlanHash, command.ApproverId, command.Attestation), new IncidentSignal(IncidentSignalKind.ApprovalRecorded, command.PlanId.ToString()), "approval", cancellationToken);
        return Result.Success();
    }
}