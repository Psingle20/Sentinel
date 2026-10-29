using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.Live;

/// <summary>One notification to send, kept until it is delivered or gives up.</summary>
public sealed record NotificationMessage(
    Guid NotificationId,
    string Channel,
    TenantId TenantId,
    IncidentId IncidentId,
    string Event,
    string Text,
    Uri? Link,
    DateTimeOffset OccurredAt);
