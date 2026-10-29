using System.Collections.Concurrent;
using System.Text.Json;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.ControlPlane;
using Sentinel.Gateway.Application.Postmortems;

namespace Sentinel.Gateway.Tests;

/// <summary>
/// The Gateway's peers as it calls them: Runtime's history, AgentWorker's executions, Executor's policy
/// and controls, the configuration owners and Ingestion's source health, with canned answers a test
/// can change and a record of what was published.
/// </summary>
public sealed class GatewayPeers : IRuntimeHistory, IAgentExecutions, IExecutorControlPlane, IConfigurationOwners, ISourceHealth
{
    public ConcurrentDictionary<IncidentId, Postmortem> Postmortems { get; } = new();

    public ConcurrentDictionary<IncidentId, IReadOnlyList<SimilarIncident>> Similar { get; } = new();

    public ConcurrentDictionary<IncidentId, string> IncidentExecutions { get; } = new();

    public ConcurrentDictionary<AgentExecutionId, string> Executions { get; } = new();

    public AgentReplayResponse ReplayAnswer { get; set; } = new(null, 400, "AgentWorker refused the replay.");

    public ControlPlaneResponse Policy { get; set; } = new(200, Json("""{"version":"policy-1"}"""), null);

    public ControlPlaneResponse PolicyPublication { get; set; } = new(409, null, "The policy changed since it was read.");

    public ControlPlaneResponse Controls { get; set; } = new(200, Json("""{"emergencyStop":false,"disabledActions":[]}"""), null);

    public ControlPlaneResponse ControlChange { get; set; } = new(204, null, null);

    public ControlPlaneResponse SourceHealth { get; set; } = new(200, Json("""[{"sourceId":"prometheus:slo_burn_rate","healthy":true}]"""), null);

    public ControlPlaneResponse Configuration { get; set; } = new(200, Json("""{"version":"deployment"}"""), null);

    public ControlPlaneResponse ConfigurationPublication { get; set; } = new(422, null, "The configuration does not satisfy the owner's rules.");

    public ConcurrentQueue<string> Published { get; } = new();

    public static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    public Task<Postmortem?> PostmortemAsync(IncidentId incidentId, CancellationToken cancellationToken) => Task.FromResult(Postmortems.GetValueOrDefault(incidentId));

    public Task<IReadOnlyList<SimilarIncident>?> SimilarAsync(IncidentId incidentId, CancellationToken cancellationToken) => Task.FromResult(Similar.GetValueOrDefault(incidentId));

    public Task<DomainReplayResult?> ReplayAsync(IncidentId incidentId, CancellationToken cancellationToken) => Task.FromResult<DomainReplayResult?>(null);

    public Task<JsonElement> ForIncidentAsync(IncidentId incidentId, CancellationToken cancellationToken) => Task.FromResult(Json(IncidentExecutions.GetValueOrDefault(incidentId) ?? "[]"));

    public Task<JsonElement?> GetAsync(AgentExecutionId executionId, CancellationToken cancellationToken) =>
        Task.FromResult(Executions.TryGetValue(executionId, out string? json) ? Json(json) : (JsonElement?)null);

    public Task<AgentReplayResponse> ReplayAsync(AgentExecutionId executionId, InvestigationReplayRequest request, CancellationToken cancellationToken) => Task.FromResult(ReplayAnswer);

    public Task<ControlPlaneResponse> PolicyAsync(CancellationToken cancellationToken) => Task.FromResult(Policy);

    public Task<ControlPlaneResponse> PolicyHistoryAsync(CancellationToken cancellationToken) => Task.FromResult(Policy);

    public Task<ControlPlaneResponse> PublishPolicyAsync(JsonElement document, string expectedVersion, string author, CancellationToken cancellationToken)
    {
        Published.Enqueue($"policy:{expectedVersion}:{author}");
        return Task.FromResult(PolicyPublication);
    }

    Task<ControlPlaneResponse> IExecutorControlPlane.ControlsAsync(CancellationToken cancellationToken) => Task.FromResult(Controls);

    public Task<ControlPlaneResponse> SetEmergencyStopAsync(bool engaged, string actor, CancellationToken cancellationToken) => Task.FromResult(ControlChange);

    public Task<ControlPlaneResponse> SetActionDisabledAsync(string actionKind, bool disabled, string actor, CancellationToken cancellationToken) => Task.FromResult(ControlChange);

    public Task<ControlPlaneResponse> CurrentAsync(string area, CancellationToken cancellationToken) => Task.FromResult(Configuration);

    public Task<ControlPlaneResponse> HistoryAsync(string area, CancellationToken cancellationToken) => Task.FromResult(Configuration);

    public Task<ControlPlaneResponse> PublishAsync(string area, JsonElement document, string expectedVersion, string author, CancellationToken cancellationToken)
    {
        Published.Enqueue($"{area}:{expectedVersion}:{author}");
        return Task.FromResult(ConfigurationPublication);
    }

    public Task<ControlPlaneResponse> SourcesAsync(CancellationToken cancellationToken) => Task.FromResult(SourceHealth);
}
