using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.ServiceDefaults;

namespace Sentinel.Ingestion.Infrastructure.Messaging;

public static class IngestionMessages
{
    /// <summary>
    /// What Ingestion exchanges: it publishes the observations that drive correlation (ADR-018) and
    /// consumes nothing.
    /// </summary>
    public static MessagingBuilder RegisterIngestionMessages(this MessagingBuilder messaging) =>
        messaging.Produces<ObservationRecorded>();
}
