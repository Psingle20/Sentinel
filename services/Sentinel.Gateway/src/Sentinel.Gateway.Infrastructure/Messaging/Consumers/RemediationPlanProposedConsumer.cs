using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.BusinessProcesses.Live;
using Sentinel.Gateway.Application.Live;

namespace Sentinel.Gateway.Infrastructure.Messaging.Consumers;

/// <summary>Runtime proposed a remediation plan. The message becomes a live event for the web app and its notifications.</summary>
public sealed class RemediationPlanProposedConsumer(IRequestDispatcher requests) : IIntegrationMessageHandler<RemediationPlanProposed>
{
    public Task<Result> HandleAsync(RemediationPlanProposed message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return requests.HandleRequestAsync(new RecordIncidentEventRequest(LiveEventMapper.From(message)), cancellationToken);
    }
}
