using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.BusinessTasks.Queries.Executions;

public sealed record ListExecutionsQuery(IncidentId IncidentId) : IQuery<IReadOnlyList<AgentExecutionRecord>>;
