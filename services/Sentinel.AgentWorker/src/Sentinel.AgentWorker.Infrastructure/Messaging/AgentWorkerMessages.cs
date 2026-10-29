using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Sentinel.AgentWorker.Infrastructure.Messaging.Consumers;
using Sentinel.Contracts.Investigations;
using Sentinel.ServiceDefaults;

namespace Sentinel.AgentWorker.Infrastructure.Messaging;

public static class AgentWorkerMessages
{
    /// <summary>What AgentWorker exchanges: Runtime's investigation requests in, the findings out.</summary>
    public static MessagingBuilder RegisterAgentWorkerMessages(this MessagingBuilder messaging) =>
        messaging
            .Produces<InvestigationCompleted>()
            .Consumes<InvestigateIncident, InvestigateIncidentConsumer>();
}
