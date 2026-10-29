using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Live;

namespace Sentinel.Gateway.Application.Abstractions.Persistence;

/// <summary>Api's notification outbox: enqueued in the inbox transaction, delivered by a dispatcher with retries.</summary>
public interface INotificationStore
{
    Task EnqueueAsync(NotificationMessage message, CancellationToken cancellationToken);

    Task<IReadOnlyList<(NotificationMessage Message, int Attempts)>> DueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    Task MarkAsync(Guid notificationId, NotificationStatus status, int attempts, string? error, DateTimeOffset? nextAttemptAt, CancellationToken cancellationToken);
}
