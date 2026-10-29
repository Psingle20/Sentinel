using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Incidents.Commands;
using Sentinel.Runtime.Application.Services;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

/// <summary>Records how Executor's execution ended through the incident's writer and signals the workflow.</summary>
public sealed class RecordExecutionResultCommandHandler(IncidentReportRecorder recorder) : ICommandHandler<RecordExecutionResultCommand>
{
    public async Task<Result> Handle(RecordExecutionResultCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await recorder.RecordAsync(
            command.IncidentId,
            new RecordExecutionCommand(command.PlanId, command.PlanVersion, command.Result, command.Detail),
            new IncidentSignal(IncidentSignalKind.ExecutionReported, command.Result.ToString()),
            "execution result",
            cancellationToken);
        return Result.Success();
    }
}
