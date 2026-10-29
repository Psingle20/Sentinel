using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Runs;

namespace Reference.Scenarios.Application.BusinessTasks.Queries.Runs;

public sealed class GetCurrentRunQueryHandler(ScenarioRunner runner) : IQueryHandler<GetCurrentRunQuery, ScenarioRunView?>
{
    public Task<Result<ScenarioRunView?>> Handle(GetCurrentRunQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(runner.Current));
}
