using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Sentinel.Contracts;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Options;

namespace Sentinel.AgentWorker.Application.Tools;

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
