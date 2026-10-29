using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Application.BusinessProcesses.Investigations;
using Sentinel.Contracts.Investigations;

namespace Sentinel.AgentWorker.Infrastructure.Messaging.Consumers;

/// <summary>Runtime's request to investigate an incident, handed to the application as a request.</summary>
public sealed class InvestigateIncidentConsumer(IRequestDispatcher requests) : IIntegrationMessageHandler<InvestigateIncident>
{
    public Task<Result> HandleAsync(InvestigateIncident message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return requests.HandleRequestAsync(new InvestigateIncidentRequest(message), cancellationToken);
    }
}
