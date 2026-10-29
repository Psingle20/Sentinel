using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Live;
using Sentinel.Gateway.Application.Options;

namespace Sentinel.Gateway.Application.Abstractions.Providers;

/// <summary>A notification provider: one way to reach people, behind Sentinel's own interface.</summary>
public interface INotificationProvider
{
    string Kind { get; }

    Task SendAsync(NotificationMessage message, NotificationChannelOptions channel, CancellationToken cancellationToken);
}
