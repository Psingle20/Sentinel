using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.BusinessProcesses.Remediation;

namespace Sentinel.Runtime.Infrastructure.Messaging.Consumers;

/// <summary>The Gateway's relayed rejection, handed to the application as a request.</summary>
public sealed class RecordRejectionConsumer(IRequestDispatcher requests) : IIntegrationMessageHandler<RecordRejection>
{
    public Task<Result> HandleAsync(RecordRejection message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return requests.HandleRequestAsync(new RecordRejectionRequest(message.IncidentId, message.PlanId, message.PlanVersion, message.RejectedBy, message.Reason), cancellationToken);
    }
}