using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;

namespace Sentinel.EndToEnd.Tests;

/// <summary>
/// Writes one scenario run for the Phase 07 evaluation harness: a schema-2 record with Runtime's run
/// record (projection, version manifest and incident memory) and every agent execution, under
/// artifacts/reports/evaluation/runs. scripts/evaluation/evaluate.py scores it against the package. The record names the
/// reference environment the run used, as the scenario runner reports it, and carries the fidelity its package declares,
/// so an emulated run is never read as AWS's own behaviour (Phase E01).
/// </summary>
public static class EvaluationRun
{
    public const string Dataset = "scenarios-v3";

    private static readonly JsonSerializerOptions Indented = new(SentinelJson.Options) { WriteIndented = true };

    public static async Task WriteAsync(SentinelSystem system, string packageId, IncidentDetail? incident, bool approved)
    {
        ArgumentNullException.ThrowIfNull(system);

        JsonNode? runRecord = null;
        JsonNode executions = new JsonArray();
        if (incident is not null)
        {
            runRecord = await system.Runtime.GetFromJsonAsync<JsonNode>($"/v1/incidents/{incident.Summary.IncidentId}/run-record?dataset={Dataset}");
            executions = await system.AgentWorker.GetFromJsonAsync<JsonNode>($"/v1/executions?incidentId={incident.Summary.IncidentId}") ?? new JsonArray();
        }

        var record = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["scenarioId"] = packageId,
            ["packageVersion"] = PackageVersion(packageId),
            ["dataset"] = Dataset,
            ["recordedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["scenarioStartedAt"] = system.ScenarioStartedAt.ToString("O"),
            ["approved"] = approved,
            ["environment"] = await system.ScenarioRunner.GetFromJsonAsync<JsonNode>("/api/environment"),
            ["fidelity"] = Package(packageId)["fidelity"]?.DeepClone(),
            ["runRecord"] = runRecord,
            ["agentExecutions"] = executions
        };

        string directory = Path.Combine(ProjectRoot(), "artifacts", "reports", "evaluation", "runs");
        Directory.CreateDirectory(directory);
        // A live model's run sits beside the test double's instead of replacing it; the harness scores each model as its own group.
        string name = SentinelSystem.ModelProvider == SentinelSystem.TestDouble ? packageId : $"{packageId}.{SentinelSystem.ModelProvider}";
        await File.WriteAllTextAsync(Path.Combine(directory, $"{name}.json"), record.ToJsonString(Indented) + "\n");
    }

    /// <summary>A scenario package from reference-system/scenarios/packages.</summary>
    public static JsonNode Package(string packageId) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(ProjectRoot(), "reference-system", "scenarios", "packages", $"{packageId}.json")))!;

    private static int PackageVersion(string packageId) => Package(packageId)["version"]!.GetValue<int>();

    public static string ProjectRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sentinel.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The project root was not found above the test output.");
    }
}
