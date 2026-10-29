using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using System.Text.Json;

namespace Sentinel.Gateway.Application.BusinessProcesses.Incidents;

/// <summary>An incident's agent executions as AgentWorker records them (Phase 08 agent activity view).</summary>
public sealed record ListIncidentAgentExecutionsRequest(IncidentId IncidentId) : IRequest<JsonElement>;
