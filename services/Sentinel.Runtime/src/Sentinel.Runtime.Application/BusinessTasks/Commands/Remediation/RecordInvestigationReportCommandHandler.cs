using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Incidents.Commands;
using Sentinel.Runtime.Application.Services;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

/// <summary>Records an investigation's findings through the incident's writer, which decides what becomes evidence, and signals the workflow.</summary>
public sealed class RecordInvestigationReportCommandHandler(IncidentReportRecorder recorder) : ICommandHandler<RecordInvestigationReportCommand>
{
    public async Task<Result> Handle(RecordInvestigationReportCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await recorder.RecordAsync(
            command.IncidentId,
            new RecordInvestigationCommand(command.Report),
            new IncidentSignal(IncidentSignalKind.InvestigationReported, command.Report.InvestigationId.ToString()),
            "investigation report",
            cancellationToken);
        return Result.Success();
    }
}
