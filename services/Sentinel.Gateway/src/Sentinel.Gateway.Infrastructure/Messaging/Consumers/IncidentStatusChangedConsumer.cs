using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Incidents;
using Sentinel.Gateway.Application.BusinessProcesses.Live;
using Sentinel.Gateway.Application.Live;

namespace Sentinel.Gateway.Infrastructure.Messaging.Consumers;

/// <summary>An incident changed status. The message becomes a live event for the web app and its notifications.</summary>
public sealed class IncidentStatusChangedConsumer(IRequestDispatcher requests) : IIntegrationMessageHandler<IncidentStatusChanged>
{
    public Task<Result> HandleAsync(IncidentStatusChanged message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return requests.HandleRequestAsync(new RecordIncidentEventRequest(LiveEventMapper.From(message)), cancellationToken);
    }
}
