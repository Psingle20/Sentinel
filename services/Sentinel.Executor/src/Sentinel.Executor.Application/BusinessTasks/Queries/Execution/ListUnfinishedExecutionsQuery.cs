using CleanArchitecture.BuildingBlocks.Messaging;

namespace Sentinel.Executor.Application.BusinessTasks.Queries.Execution;

public sealed record ListUnfinishedExecutionsQuery : IQuery<IReadOnlyList<string>>;
