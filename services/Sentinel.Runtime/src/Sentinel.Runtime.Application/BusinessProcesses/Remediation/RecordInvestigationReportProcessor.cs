using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.BusinessProcesses.Remediation;

public sealed class RecordInvestigationReportProcessor(ICommandDispatcher commands) : IProcessor<RecordInvestigationReportRequest>
{
    public Task<Result> Process(RecordInvestigationReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new RecordInvestigationReportCommand(request.IncidentId, request.Report), cancellationToken);
    }
}
