using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Gateway.Application.Live;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;

namespace Sentinel.Gateway.Application.ControlPlane;

/// <summary>Configured as <c>Gateway:Tenants</c>: the tenants Sentinel serves and their environments (Phase 08 tenant management).</summary>
public sealed class TenantCatalogOptions
{
    public const string Section = "Gateway:Tenants";

    public IList<TenantOptions> Tenants { get; } = [];
}

public sealed class TenantOptions
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public IList<string> Environments { get; } = [];
}

/// <summary>
/// Api's control-plane document (Phase 08): the tenant and environment catalog, and notification
/// channels and routes. A webhook channel can target only a host the deployment allows, and names
/// the configuration key of its signing secret rather than the secret, so the document never holds
/// a credential and an operator cannot point Sentinel at an arbitrary address.
/// </summary>
public sealed record ApiConfigurationDocument : IVersionedDocument
{
    public required string Version { get; init; }

    public IReadOnlyList<TenantSetting> Tenants { get; init; } = [];

    public IReadOnlyList<NotificationChannelSetting> Channels { get; init; } = [];

    public IReadOnlyList<NotificationRouteSetting> Routes { get; init; } = [];
}

public sealed record TenantSetting(string Id, string Name, IReadOnlyList<string> Environments);

public sealed record NotificationChannelSetting(string Name, string Kind, Uri? Url, string? SecretKey);

public sealed record NotificationRouteSetting(string Event, Severity? MinimumSeverity, string Channel, string? Tenant = null);

/// <summary>Moves Api's options to and from the control-plane document.</summary>
public static class ApiConfigurationMapping
{
    public const string DeploymentVersion = "deployment";

    private static readonly string[] Kinds = ["log", "webhook"];

    public static ApiConfigurationDocument From(TenantCatalogOptions tenants, NotificationOptions notifications, string version = DeploymentVersion)
    {
        ArgumentNullException.ThrowIfNull(tenants);
        ArgumentNullException.ThrowIfNull(notifications);

        return new ApiConfigurationDocument
        {
            Version = version,
            Tenants = [.. tenants.Tenants.Select(tenant => new TenantSetting(tenant.Id, tenant.Name, [.. tenant.Environments]))],
            Channels = [.. notifications.Channels.Select(channel => new NotificationChannelSetting(channel.Name, channel.Kind, channel.Url, channel.SecretKey))],
            Routes = [.. notifications.Routes.Select(route => new NotificationRouteSetting(route.Event, route.MinimumSeverity, route.Channel, route.Tenant))]
        };
    }

    public static void Apply(TenantCatalogOptions options, ApiConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(document);

        options.Tenants.Clear();
        foreach (TenantSetting tenant in document.Tenants)
        {
            var entry = new TenantOptions { Id = tenant.Id, Name = tenant.Name };
            foreach (string environment in tenant.Environments)
            {
                entry.Environments.Add(environment);
            }

            options.Tenants.Add(entry);
        }
    }

    public static void Apply(NotificationOptions options, ApiConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(document);

        options.Channels.Clear();
        foreach (NotificationChannelSetting channel in document.Channels)
        {
            options.Channels.Add(new NotificationChannelOptions { Name = channel.Name, Kind = channel.Kind, Url = channel.Url, SecretKey = channel.SecretKey });
        }

        options.Routes.Clear();
        foreach (NotificationRouteSetting route in document.Routes)
        {
            options.Routes.Add(new NotificationRouteOptions { Event = route.Event, MinimumSeverity = route.MinimumSeverity, Channel = route.Channel, Tenant = route.Tenant });
        }
    }

    // HTTPS everywhere; plain HTTP only to a receiver on the same machine.
    private static bool Secure(Uri url) => url.Scheme == Uri.UriSchemeHttps || url.IsLoopback && url.Scheme == Uri.UriSchemeHttp;

    /// <summary>Why the document cannot run in this deployment; empty when it can.</summary>
    public static IReadOnlyList<string> Problems(ApiConfigurationDocument document, IReadOnlyCollection<string> allowedWebhookHosts, Func<string, bool> secretExists)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(allowedWebhookHosts);
        ArgumentNullException.ThrowIfNull(secretExists);

        var problems = new List<string>();
        if (document.Tenants.Any(tenant => string.IsNullOrWhiteSpace(tenant.Id) || tenant.Environments.Count == 0 || tenant.Environments.Any(string.IsNullOrWhiteSpace)))
        {
            problems.Add("Every tenant has an ID and at least one named environment.");
        }

        if (document.Tenants.GroupBy(tenant => tenant.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            problems.Add("Tenant IDs are unique.");
        }

        if (document.Channels.GroupBy(channel => channel.Name, StringComparer.Ordinal).Any(group => group.Count() > 1 || string.IsNullOrWhiteSpace(group.Key)))
        {
            problems.Add("Every channel has a unique name.");
        }

        foreach (NotificationChannelSetting channel in document.Channels)
        {
            if (!Kinds.Contains(channel.Kind, StringComparer.Ordinal))
            {
                problems.Add($"Channel '{channel.Name}' has kind '{channel.Kind}'; the kinds are {string.Join(" and ", Kinds)}.");
            }
            else if (channel.Kind == "webhook")
            {
                if (channel.Url is not { IsAbsoluteUri: true } url || !Secure(url))
                {
                    problems.Add($"Webhook channel '{channel.Name}' needs an absolute HTTPS address (HTTP only on loopback).");
                }
                else if (!allowedWebhookHosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase))
                {
                    problems.Add($"Webhook host '{url.Host}' is not in this deployment's allowed webhook hosts.");
                }

                if (channel.SecretKey is not { Length: > 0 } key || !secretExists(key))
                {
                    problems.Add($"Webhook channel '{channel.Name}' names the configuration key of a signing secret that this deployment has.");
                }
            }
        }

        foreach (NotificationRouteSetting route in document.Routes)
        {
            if (!LiveEventKinds.All.Contains(route.Event, StringComparer.Ordinal))
            {
                problems.Add($"Route event '{route.Event}' is not a live event; the events are {string.Join(", ", LiveEventKinds.All)}.");
            }

            if (!document.Channels.Any(channel => channel.Name == route.Channel))
            {
                problems.Add($"A route for '{route.Event}' names channel '{route.Channel}', which is not defined.");
            }

            if (route.Tenant is { } tenant && !document.Tenants.Any(candidate => candidate.Id == tenant))
            {
                problems.Add($"A route for '{route.Event}' is limited to tenant '{tenant}', which is not in the catalog.");
            }
        }

        return problems;
    }
}

/// <summary>Reads and publishes Api's control-plane document; the deployment configuration seeds it.</summary>
public sealed class ApiConfigurationService(
    VersionedDocumentPublisher<ApiConfigurationDocument> publisher,
    Func<ApiConfigurationDocument> seed,
    IReadOnlyCollection<string> allowedWebhookHosts,
    Func<string, bool> secretExists)
{
    public Task<DocumentState<ApiConfigurationDocument>> CurrentAsync(CancellationToken cancellationToken) =>
        publisher.CurrentAsync(seed, cancellationToken);

    public Task<IReadOnlyList<DocumentVersion>> HistoryAsync(CancellationToken cancellationToken) =>
        publisher.HistoryAsync(seed, 100, cancellationToken);

    public Task<Result<PublishedDocument<ApiConfigurationDocument>>> PublishAsync(ApiConfigurationDocument document, string expectedVersion, string author, CancellationToken cancellationToken) =>
        publisher.PublishAsync(document, expectedVersion, author, seed, candidate => ApiConfigurationMapping.Problems(candidate, allowedWebhookHosts, secretExists), cancellationToken);
}
