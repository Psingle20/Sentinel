using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.AgentWorker.Application.Abstractions.Providers;

namespace Sentinel.AgentWorker.Application.Tools;

/// <summary>The slowest and failed spans for a service, with the service each one waited on.</summary>
public sealed class SlowSpansTool(IObservationQueries observations) : IInvestigationTool
{
    public const string Name = "slow_spans";

    public ModelToolDefinition Definition => new(
        Name,
        "The slowest and failed spans of a service in the investigation window, including which downstream service each one called and how long it took.",
        """
        {"type":"object","properties":{
          "service":{"type":"string"},
          "from":{"type":"string","format":"date-time"},
          "to":{"type":"string","format":"date-time"}},
         "required":["service"],"additionalProperties":false}
        """);

    public async Task<ToolOutcome> InvokeAsync(ToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!ToolArguments.TryString(arguments, "service", out string? service) || !ToolArguments.TryService(service!, out ServiceId serviceId))
        {
            return ToolOutcome.Failure("service must be a service name.");
        }

        TimeRange range = ToolArguments.Range(arguments, context.Window);
        ObservationQueryResult result = await observations.QueryAsync(new ObservationQuery
        {
            TenantId = context.TenantId,
            EnvironmentId = context.EnvironmentId,
            ServiceId = serviceId,
            Kind = ObservationKind.Trace,
            Range = range,
            Limit = 2000
        }, cancellationToken);

        TraceObservation[] spans = [.. result.Observations.OfType<TraceObservation>()
            .OrderByDescending(span => span.Status == SpanStatus.Error)
            .ThenByDescending(span => span.Duration)
            .Take(15)];
        var content = new JsonObject
        {
            ["service"] = serviceId.Value,
            ["metadata"] = ToolArguments.Metadata(result.Metadata),
            ["spans"] = new JsonArray([.. spans.Select(span => (JsonNode)new JsonObject
            {
                ["id"] = span.ObservationId.Value,
                ["t"] = span.EventTime.ToString("O", CultureInfo.InvariantCulture),
                ["operation"] = span.OperationName,
                ["durationMs"] = Math.Round(span.Duration.TotalMilliseconds),
                ["status"] = span.Status.ToString(),
                ["calls"] = span.DependencyServiceId?.Value
            })])
        };

        return new ToolOutcome(content.ToJsonString(), [.. spans.Select(span => span.ObservationId)], ToolArguments.IsPartial(result.Metadata, range));
    }
}
