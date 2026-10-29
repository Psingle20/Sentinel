using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using System.Text.Json;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Incidents;

public sealed record ListIncidentAgentExecutionsQuery(IncidentId IncidentId) : IQuery<JsonElement>;
