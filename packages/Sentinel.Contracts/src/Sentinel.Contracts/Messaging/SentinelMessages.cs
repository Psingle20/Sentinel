using System.Text.Json;
using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Messaging;

/// <summary>
/// Implemented by every integration message. The static members are the registration a
/// service passes to <c>Produces</c> or <c>Consumes</c> (ADR-012); the instance members carry
/// the identity every Sentinel message must have.
/// </summary>
public interface ISentinelMessage
{
    static abstract string MessageType { get; }

    static abstract int SchemaVersion { get; }

    static abstract string Stream { get; }

    TenantId TenantId { get; }

    EnvironmentId EnvironmentId { get; }
}

/// <summary>Stream names, following the topic families in architecture §56.</summary>
public static class SentinelStreams
{
    public const string Observations = "sentinel.observations";
    public const string Incidents = "sentinel.incidents";
    public const string Investigations = "sentinel.investigations";
    public const string Remediation = "sentinel.remediation";
    public const string AgentWorkerCommands = "sentinel.agentworker.commands";
    public const string RuntimeCommands = "sentinel.runtime.commands";
    public const string ExecutorCommands = "sentinel.executor.commands";
}

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

/// <summary>
/// The serialization every channel uses for Sentinel contracts: camelCase, enums as camelCase
/// strings, nulls omitted (ADR-012). It matches the building blocks' message serializer.
/// </summary>
public static class SentinelJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
