using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Models;
using Sentinel.Contracts;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Application.Tools;

/// <summary>
/// What a tool call may touch. Tenant, environment and the time window come from the investigation
/// command, never from model arguments, so a model cannot widen its own scope.
/// </summary>
public sealed record ToolContext(
    TenantId TenantId,
    EnvironmentId EnvironmentId,
    IncidentId IncidentId,
    ServiceId AffectedServiceId,
    IReadOnlyList<ServiceId> RelatedServiceIds,
    TimeRange Window);

/// <summary>
/// A tool's answer: the content returned to the model, the observation IDs it contained (the only
/// IDs findings may cite) and whether the data behind it was partial.
/// </summary>
public sealed record ToolOutcome(string Content, IReadOnlyList<ObservationId> ObservationIds, bool Partial, string? Error = null)
{
    public static ToolOutcome Failure(string error) =>
        new(JsonSerializer.Serialize(new { error }, SentinelJson.Options), [], true, error);
}

/// <summary>A typed, read-only investigation tool. Tools read only through Ingestion's query contract (ADR-007).</summary>
public interface IInvestigationTool
{
    ModelToolDefinition Definition { get; }

    Task<ToolOutcome> InvokeAsync(ToolContext context, JsonElement arguments, CancellationToken cancellationToken);
}

/// <summary>Ingestion's observation query contract, as AgentWorker sees it.</summary>
public interface IObservationQueries
{
    Task<ObservationQueryResult> QueryAsync(ObservationQuery query, CancellationToken cancellationToken);
}

/// <summary>Configured as <c>Investigation</c>.</summary>
public sealed class InvestigationOptions
{
    public const string Section = "Investigation";

    /// <summary><c>model</c> (the generalist investigator) or <c>deploy-blame</c> (the baseline).</summary>
    public string Investigator { get; set; } = "model";

    /// <summary>Runs the deploy-blame baseline in shadow on every investigation, for comparison.</summary>
    public bool ShadowBaseline { get; set; } = true;

    /// <summary>The investigator prompt version (<see cref="Investigations.InvestigatorPrompt.Versions"/>); every execution records it.</summary>
    public string PromptVersion { get; set; } = Investigations.InvestigatorPrompt.Version;

    public int MaxModelTurns { get; set; } = 6;

    public int MaxOutputTokens { get; set; } = 4_000;

    /// <summary>The metric names tools may query, with what each measures; shown to the model.</summary>
    public IDictionary<string, string> Metrics { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Most points per series returned to the model; longer series are down-sampled.</summary>
    public int MaxPointsPerSeries { get; set; } = 40;
}

public sealed class MetricRangeTool(IObservationQueries observations, IOptions<InvestigationOptions> options) : IInvestigationTool
{
    public const string Name = "metric_range";

    public ModelToolDefinition Definition => new(
        Name,
        "Read one metric over the investigation window, for one service or for every service that reports it. Returns samples with observation IDs.",
        """
        {"type":"object","properties":{
          "metric":{"type":"string","description":"A metric name from the catalog."},
          "service":{"type":"string","description":"Optional service; omit to get every service's series."},
          "from":{"type":"string","format":"date-time"},
          "to":{"type":"string","format":"date-time"}},
         "required":["metric"],"additionalProperties":false}
        """);

    public async Task<ToolOutcome> InvokeAsync(ToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!ToolArguments.TryString(arguments, "metric", out string? metric) || !options.Value.Metrics.ContainsKey(metric!))
        {
            return ToolOutcome.Failure($"metric must be one of: {string.Join(", ", options.Value.Metrics.Keys)}.");
        }

        ServiceId? service = null;
        if (ToolArguments.TryString(arguments, "service", out string? requested))
        {
            if (!ToolArguments.TryService(requested!, out ServiceId parsed))
            {
                return ToolOutcome.Failure($"'{requested}' is not a service name.");
            }

            service = parsed;
        }

        TimeRange range = ToolArguments.Range(arguments, context.Window);
        ObservationQueryResult result = await observations.QueryAsync(new ObservationQuery
        {
            TenantId = context.TenantId,
            EnvironmentId = context.EnvironmentId,
            ServiceId = service,
            Kind = ObservationKind.Metric,
            Range = range,
            Filters = new Dictionary<string, string> { ["metricName"] = metric! },
            Limit = 4000
        }, cancellationToken);

        MetricObservation[] samples = [.. result.Observations.OfType<MetricObservation>()];
        var series = new JsonArray();
        var cited = new List<ObservationId>();
        foreach (IGrouping<ServiceId, MetricObservation> group in samples.GroupBy(sample => sample.ServiceId).OrderBy(group => group.Key.Value, StringComparer.Ordinal))
        {
            MetricObservation[] points = DownSample([.. group.OrderBy(sample => sample.EventTime)], options.Value.MaxPointsPerSeries);
            cited.AddRange(points.Select(point => point.ObservationId));
            series.Add(new JsonObject
            {
                ["service"] = group.Key.Value,
                ["unit"] = points.FirstOrDefault()?.Unit,
                ["samples"] = new JsonArray([.. points.Select(point => (JsonNode)new JsonObject
                {
                    ["id"] = point.ObservationId.Value,
                    ["t"] = point.EventTime.ToString("O", CultureInfo.InvariantCulture),
                    ["v"] = Math.Round(point.Value, 4)
                })])
            });
        }

        var content = new JsonObject
        {
            ["metric"] = metric,
            ["from"] = range.From.ToString("O", CultureInfo.InvariantCulture),
            ["to"] = range.To.ToString("O", CultureInfo.InvariantCulture),
            ["metadata"] = ToolArguments.Metadata(result.Metadata),
            ["series"] = series
        };

        return new ToolOutcome(content.ToJsonString(), cited, ToolArguments.IsPartial(result.Metadata, range));
    }

    /// <summary>Keeps the first and last points, every local extreme it can, and an even spread of the rest.</summary>
    public static MetricObservation[] DownSample(MetricObservation[] points, int maximum)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Length <= maximum)
        {
            return points;
        }

        SortedSet<int> keep =
        [
            0,
            points.Length - 1,
            Array.IndexOf(points, points.MaxBy(point => point.Value)!),
            Array.IndexOf(points, points.MinBy(point => point.Value)!)
        ];
        double stride = (double)(points.Length - 1) / (maximum - keep.Count);
        for (double index = stride; keep.Count < maximum && index < points.Length - 1; index += stride)
        {
            keep.Add((int)Math.Round(index));
        }

        return [.. keep.Select(index => points[index])];
    }
}

public sealed class RecentDeploymentsTool(IObservationQueries observations) : IInvestigationTool
{
    public const string Name = "recent_deployments";

    public ModelToolDefinition Definition => new(
        Name,
        "List deployments and configuration changes in the investigation window, optionally only for some services. Each change reports when it started and when its new version began serving.",
        """
        {"type":"object","properties":{
          "services":{"type":"array","items":{"type":"string"}},
          "from":{"type":"string","format":"date-time"},
          "to":{"type":"string","format":"date-time"}},
         "additionalProperties":false}
        """);

    public async Task<ToolOutcome> InvokeAsync(ToolContext context, JsonElement arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        HashSet<string>? services = null;
        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("services", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            services = [.. list.EnumerateArray().Select(item => item.GetString()).OfType<string>()];
        }

        TimeRange range = ToolArguments.Range(arguments, context.Window);
        ObservationQueryResult result = await observations.QueryAsync(new ObservationQuery
        {
            TenantId = context.TenantId,
            EnvironmentId = context.EnvironmentId,
            Kind = ObservationKind.Deployment,
            Range = range,
            Limit = 500
        }, cancellationToken);

        DeploymentObservation[] deployments = [.. result.Observations.OfType<DeploymentObservation>()
            .Where(deployment => services is null || services.Contains(deployment.ServiceId.Value))
            .OrderBy(deployment => deployment.EventTime)];

        var content = new JsonObject
        {
            ["from"] = range.From.ToString("O", CultureInfo.InvariantCulture),
            ["to"] = range.To.ToString("O", CultureInfo.InvariantCulture),
            ["metadata"] = ToolArguments.Metadata(result.Metadata),
            ["changes"] = new JsonArray([.. deployments.Select(deployment => (JsonNode)new JsonObject
            {
                ["id"] = deployment.ObservationId.Value,
                ["deploymentId"] = deployment.DeploymentId,
                ["service"] = deployment.ServiceId.Value,
                ["kind"] = deployment.ChangeKind,
                ["status"] = deployment.Status,
                ["version"] = deployment.Version,
                ["previousVersion"] = deployment.PreviousVersion,
                ["startedAt"] = deployment.StartedAt.ToString("O", CultureInfo.InvariantCulture),
                ["servingFrom"] = deployment.CompletedAt?.ToString("O", CultureInfo.InvariantCulture),
                ["actor"] = deployment.Actor
            })])
        };

        return new ToolOutcome(content.ToJsonString(), [.. deployments.Select(deployment => deployment.ObservationId)], ToolArguments.IsPartial(result.Metadata, range));
    }
}

internal static class ToolArguments
{
    public static bool TryString(JsonElement arguments, string name, out string? value)
    {
        value = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
        return !string.IsNullOrWhiteSpace(value);
    }

    public static bool TryService(string value, out ServiceId service)
    {
        service = default;
        try
        {
            service = ServiceId.From(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The requested range, clamped to the investigation window.</summary>
    public static TimeRange Range(JsonElement arguments, TimeRange window)
    {
        DateTimeOffset from = TryTime(arguments, "from") is { } requestedFrom && requestedFrom > window.From ? requestedFrom : window.From;
        DateTimeOffset to = TryTime(arguments, "to") is { } requestedTo && requestedTo < window.To ? requestedTo : window.To;
        return to > from ? new TimeRange(from, to) : window;
    }

    public static JsonObject Metadata(QueryMetadata metadata) => new()
    {
        ["observedThrough"] = metadata.ObservedThrough.ToString("O", CultureInfo.InvariantCulture),
        ["completeness"] = metadata.Completeness.ToString().ToLowerInvariant(),
        ["truncated"] = metadata.Truncated
    };

    public static bool IsPartial(QueryMetadata metadata, TimeRange range) =>
        metadata.Completeness != QueryCompleteness.Complete || metadata.Truncated || metadata.ObservedThrough < range.To;

    private static DateTimeOffset? TryTime(JsonElement arguments, string name) =>
        TryString(arguments, name, out string? value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed
            : null;
}
