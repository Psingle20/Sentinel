using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Infrastructure.Messaging.Consumers;
using Sentinel.ServiceDefaults;

namespace Sentinel.Executor.Infrastructure.Messaging;

public static class ExecutorMessages
{
    /// <summary>What Executor exchanges: Runtime's execution requests in, the execution results out.</summary>
    public static MessagingBuilder RegisterExecutorMessages(this MessagingBuilder messaging) =>
        messaging
            .Produces<RemediationExecuted>()
            .Consumes<ExecuteRemediation, ExecuteRemediationConsumer>();
}