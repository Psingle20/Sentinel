using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.AgentWorker.Application.Abstractions.Providers;

namespace Sentinel.AgentWorker.Application.Tools;

/// <summary>Observed dependency edges between services, with call counts.</summary>
public sealed class ServiceDependenciesTool(IObservationQueries observations) : IInvestigationTool
{
    public const string Name = "service_dependencies";

    public ModelToolDefinition Definition => new(
        Name,
        "Which services call which, as observed from traces in the investigation window.",
        """{"type":"object","properties":{},"additionalProperties":false}""");

    public async Task<ToolOutcome> InvokeAsync(ToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        ObservationQueryResult result = await observations.QueryAsync(new ObservationQuery
        {
            TenantId = context.TenantId,
            EnvironmentId = context.EnvironmentId,
            Kind = ObservationKind.Topology,
            Range = context.Window,
            Limit = 500
        }, cancellationToken);

        TopologyObservation[] edges = [.. result.Observations.OfType<TopologyObservation>()
            .GroupBy(edge => (edge.ServiceId, edge.TargetServiceId))
            .Select(group => group.OrderByDescending(edge => edge.EventTime).First())];
        var content = new JsonObject
        {
            ["metadata"] = ToolArguments.Metadata(result.Metadata),
            ["edges"] = new JsonArray([.. edges.Select(edge => (JsonNode)new JsonObject
            {
                ["id"] = edge.ObservationId.Value,
                ["from"] = edge.ServiceId.Value,
                ["to"] = edge.TargetServiceId.Value,
                ["confidence"] = edge.Confidence,
                ["calls"] = edge.Attributes.GetValueOrDefault("callCount")
            })])
        };

        return new ToolOutcome(content.ToJsonString(), [.. edges.Select(edge => edge.ObservationId)], ToolArguments.IsPartial(result.Metadata, context.Window));
    }
}
