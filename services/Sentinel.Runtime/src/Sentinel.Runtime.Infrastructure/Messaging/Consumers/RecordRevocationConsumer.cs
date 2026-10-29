using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.BusinessProcesses.Remediation;

namespace Sentinel.Runtime.Infrastructure.Messaging.Consumers;

/// <summary>
/// A revocation sent as a message before revocation became a request to Runtime (ADR-006); still drained from its
/// stream and verified like any other, its answer only logged because nobody waits for it.
/// </summary>
public sealed class RecordRevocationConsumer(IRequestDispatcher requests) : IIntegrationMessageHandler<RecordRevocation>
{
    public async Task<Result> HandleAsync(RecordRevocation message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        Result<RevocationResponse> answer = await requests.HandleRequestAsync(new RecordRevocationRequest(message.IncidentId, message.PlanId, message.PlanVersion, message.RevokedBy, message.Reason, message.Attestation), cancellationToken);
        return answer.IsFailure ? Result.Failure(answer.Error) : Result.Success();
    }
}
