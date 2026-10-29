using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.BusinessProcesses.Remediation;
using Sentinel.Runtime.Application.Projections;

namespace Sentinel.Runtime.Infrastructure.Messaging.Consumers;

/// <summary>Executor's execution result, handed to the application as a request.</summary>
public sealed class RemediationExecutedConsumer(IRequestDispatcher requests) : IIntegrationMessageHandler<RemediationExecuted>
{
    public Task<Result> HandleAsync(RemediationExecuted message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return requests.HandleRequestAsync(new RecordExecutionResultRequest(message.IncidentId, message.PlanId, message.PlanVersion, message.Outcome.ToDomain(), message.Detail), cancellationToken);
    }
}
