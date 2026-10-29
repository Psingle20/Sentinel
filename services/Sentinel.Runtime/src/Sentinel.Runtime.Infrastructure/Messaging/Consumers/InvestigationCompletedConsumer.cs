using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Investigations;
using Sentinel.Runtime.Application.BusinessProcesses.Remediation;
using Sentinel.Runtime.Application.Projections;

namespace Sentinel.Runtime.Infrastructure.Messaging.Consumers;

/// <summary>AgentWorker's findings, handed to the application as a request.</summary>
public sealed class InvestigationCompletedConsumer(IRequestDispatcher requests) : IIntegrationMessageHandler<InvestigationCompleted>
{
    public Task<Result> HandleAsync(InvestigationCompleted message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return requests.HandleRequestAsync(new RecordInvestigationReportRequest(message.IncidentId, message.ToReport()), cancellationToken);
    }
}
