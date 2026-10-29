using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Gateway.Application.Live;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.Options;

namespace Sentinel.Gateway.Infrastructure.Notifications;

/// <summary>The local channel: notifications appear in Api's log and the Aspire dashboard.</summary>
internal sealed class LogNotificationProvider(ILogger<LogNotificationProvider> logger) : INotificationProvider
{
    public string Kind => "log";

    public Task SendAsync(NotificationMessage message, NotificationChannelOptions channel, CancellationToken cancellationToken)
    {
        logger.LogInformation("Notification to {Channel} for incident {IncidentId} ({Event}): {Text} {Link}", channel.Name, message.IncidentId, message.Event, message.Text, message.Link);
        return Task.CompletedTask;
    }
}
