using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.BusinessProcesses.Executions;

/// <summary>An incident's agent executions: investigator, model, tool calls and cost (Phase 08 agent activity view).</summary>
public sealed record ListExecutionsRequest(IncidentId IncidentId) : IRequest<IReadOnlyList<AgentExecutionRecord>>;
