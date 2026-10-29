using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application.BusinessProcesses.Execution;

namespace Sentinel.Executor.Infrastructure.Messaging.Consumers;

/// <summary>Runtime's execution request, handed to the application as a request.</summary>
public sealed class ExecuteRemediationConsumer(IRequestDispatcher requests) : IIntegrationMessageHandler<ExecuteRemediation>
{
    public Task<Result> HandleAsync(ExecuteRemediation message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return requests.HandleRequestAsync(new ExecuteRemediationRequest(message), cancellationToken);
    }
}