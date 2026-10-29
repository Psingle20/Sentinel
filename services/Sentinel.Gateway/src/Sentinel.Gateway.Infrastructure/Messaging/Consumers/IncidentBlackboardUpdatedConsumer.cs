using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Gateway.Application.BusinessProcesses.Live;
using Sentinel.Gateway.Application.Live;

namespace Sentinel.Gateway.Infrastructure.Messaging.Consumers;

/// <summary>An incident's evidence or hypotheses changed. The message becomes a live event for the web app and its notifications.</summary>
public sealed class IncidentBlackboardUpdatedConsumer(IRequestDispatcher requests) : IIntegrationMessageHandler<IncidentBlackboardUpdated>
{
    public Task<Result> HandleAsync(IncidentBlackboardUpdated message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return requests.HandleRequestAsync(new RecordIncidentEventRequest(LiveEventMapper.From(message)), cancellationToken);
    }
}
