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

namespace Sentinel.AgentWorker.Application.Tools;

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
