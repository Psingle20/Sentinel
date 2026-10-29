using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.BusinessProcesses.Remediation;

namespace Sentinel.Runtime.Infrastructure.Messaging.Consumers;

/// <summary>The Gateway's relayed approval, handed to the application as a request.</summary>
public sealed class RecordApprovalConsumer(IRequestDispatcher requests) : IIntegrationMessageHandler<RecordApproval>
{
    public Task<Result> HandleAsync(RecordApproval message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return requests.HandleRequestAsync(new RecordApprovalRequest(message.IncidentId, message.PlanId, message.PlanVersion, message.PlanHash, message.ApproverId, message.Attestation), cancellationToken);
    }
}