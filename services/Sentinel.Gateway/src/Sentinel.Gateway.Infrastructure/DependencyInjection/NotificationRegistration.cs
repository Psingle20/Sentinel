using Microsoft.Extensions.DependencyInjection;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Infrastructure.Notifications;

namespace Sentinel.Gateway.Infrastructure.DependencyInjection;

internal static class NotificationRegistration
{
    /// <summary>Notifications (Phase 08): the providers and the dispatcher that delivers the notification outbox.</summary>
    public static IServiceCollection AddGatewayNotifications(this IServiceCollection services)
    {
        services.AddSingleton<INotificationProvider, LogNotificationProvider>();
        services.AddHttpClient<WebhookNotificationProvider>();
        services.AddSingleton<INotificationProvider>(provider => provider.GetRequiredService<WebhookNotificationProvider>());
        services.AddHostedService<NotificationDispatcher>();
        return services;
    }
}
