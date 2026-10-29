using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Runs;

namespace Reference.Scenarios.Application.BusinessTasks.Queries.Runs;

public sealed class GetRunQueryHandler(ScenarioRunner runner) : IQueryHandler<GetRunQuery, ScenarioRunView?>
{
    public Task<Result<ScenarioRunView?>> Handle(GetRunQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult(Result.Success(runner.Find(query.RunId)));
    }
}
