using System.Text.Json;
using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Messaging;

/// <summary>Identity headers carried by every Sentinel message (ADR-012).</summary>
public static class SentinelHeaders
{
    public const string TenantId = "sentinel-tenant-id";
    public const string EnvironmentId = "sentinel-environment-id";
    public const string ServiceId = "sentinel-service-id";
    public const string IncidentId = "sentinel-incident-id";

    public static IReadOnlyDictionary<string, string> For(
        TenantId tenantId,
        EnvironmentId environmentId,
        ServiceId? serviceId = null,
        IncidentId? incidentId = null)
    {
        var headers = new Dictionary<string, string>
        {
            [TenantId] = tenantId.Value,
            [EnvironmentId] = environmentId.Value
        };

        if (serviceId is { } service)
        {
            headers[ServiceId] = service.Value;
        }

        if (incidentId is { } incident)
        {
            headers[IncidentId] = incident.ToString();
        }

        return headers;
    }

    /// <summary>True when the headers carry the same tenant and environment as the payload.</summary>
    public static bool MatchIdentity(IReadOnlyDictionary<string, string> headers, ISentinelMessage message)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(message);

        return headers.TryGetValue(TenantId, out string? tenant)
            && tenant == message.TenantId.Value
            && headers.TryGetValue(EnvironmentId, out string? environment)
            && environment == message.EnvironmentId.Value;
    }
}
