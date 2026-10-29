using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Runtime.Application.BusinessProcesses.Detection;

namespace Sentinel.Runtime.Infrastructure.Messaging.Consumers;

/// <summary>Ingestion's recorded observation, handed to the application as a request.</summary>
public sealed class ObservationRecordedConsumer(IRequestDispatcher requests) : IIntegrationMessageHandler<ObservationRecorded>
{
    public Task<Result> HandleAsync(ObservationRecorded message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        return requests.HandleRequestAsync(new RecordObservationRequest(message.Observation), cancellationToken);
    }
}
