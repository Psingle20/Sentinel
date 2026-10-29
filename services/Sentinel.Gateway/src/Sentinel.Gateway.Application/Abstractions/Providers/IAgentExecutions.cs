using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.Abstractions.Providers;

/// <summary>AgentWorker's agent executions and their replays (Phase 07), passed through as AgentWorker publishes them.</summary>
public interface IAgentExecutions
{
    Task<JsonElement> ForIncidentAsync(IncidentId incidentId, CancellationToken cancellationToken);

    /// <summary>One execution, or null when AgentWorker has none with that ID.</summary>
    Task<JsonElement?> GetAsync(AgentExecutionId executionId, CancellationToken cancellationToken);

    Task<AgentReplayResponse> ReplayAsync(AgentExecutionId executionId, InvestigationReplayRequest request, CancellationToken cancellationToken);
}
