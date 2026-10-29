using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.BusinessProcesses.Remediation;

namespace Sentinel.Runtime.Infrastructure.Messaging.Consumers;

/// <summary>The Gateway's relayed revocation, handed to the application as a request.</summary>
public sealed class RecordRevocationConsumer(IRequestDispatcher requests) : IIntegrationMessageHandler<RecordRevocation>
{
    public Task<Result> HandleAsync(RecordRevocation message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return requests.HandleRequestAsync(new RecordRevocationRequest(message.IncidentId, message.PlanId, message.PlanVersion, message.RevokedBy, message.Reason), cancellationToken);
    }
}
