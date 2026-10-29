using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.Abstractions.Persistence;

/// <summary>AgentWorker's own audit store for executions, their tool calls, their captured inputs and their replays (ADR-007, Phase 07).</summary>
public interface IExecutionStore
{
    Task SaveAsync(AgentExecutionRecord execution, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentExecutionRecord>> ForIncidentAsync(IncidentId incidentId, CancellationToken cancellationToken);

    Task<AgentExecutionRecord?> GetAsync(AgentExecutionId executionId, CancellationToken cancellationToken);

    Task SaveInputsAsync(ExecutionInputs inputs, CancellationToken cancellationToken);

    Task<ExecutionInputs?> GetInputsAsync(AgentExecutionId executionId, CancellationToken cancellationToken);

    Task SaveReplayAsync(Sentinel.Contracts.Incidents.InvestigationReplayResult replay, CancellationToken cancellationToken);
}
