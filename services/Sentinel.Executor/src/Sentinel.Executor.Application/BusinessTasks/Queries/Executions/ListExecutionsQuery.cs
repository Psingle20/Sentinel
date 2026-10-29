using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Domain.Executions;

namespace Sentinel.Executor.Application.BusinessTasks.Queries.Executions;

public sealed record ListExecutionsQuery(int Limit) : IQuery<IReadOnlyList<ExecutionSnapshot>>;
