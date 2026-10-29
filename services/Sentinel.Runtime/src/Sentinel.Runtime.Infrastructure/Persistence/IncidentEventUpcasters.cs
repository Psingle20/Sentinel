using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;
using RecoveryEvaluated = Sentinel.Runtime.Domain.Incidents.Events.RecoveryEvaluated;

namespace Sentinel.Runtime.Infrastructure.Persistence;

/// <summary>
/// Upcasters for stored event payloads (Phase 07): a payload written by an older shape is completed
/// before it is deserialized, so the stream never has to be rewritten. Each upcaster is pure and
/// idempotent, and only fills what the older shape lacked.
/// </summary>
internal static class IncidentEventUpcasters
{
    public static string Upcast(string name, string payload) => name switch
    {
        "incident.detected.v1" => TriggerMetricFromTitle(payload),
        _ => payload
    };

    /// <summary>Streams detected before Phase 07 carry the trigger metric only in the title, <c>{service} {metric} at {value}</c>.</summary>
    private static string TriggerMetricFromTitle(string payload)
    {
        if (System.Text.Json.Nodes.JsonNode.Parse(payload) is not System.Text.Json.Nodes.JsonObject detected || detected["triggerMetric"] is not null)
        {
            return payload;
        }

        string[] words = (detected["title"]?.GetValue<string>() ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 4 || words[^2] != "at")
        {
            return payload;
        }

        detected["triggerMetric"] = words[1];
        return detected.ToJsonString();
    }
}
