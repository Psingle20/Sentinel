using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessTasks.Queries.Runs;
using Reference.Scenarios.Application.Runs;

namespace Reference.Scenarios.Application.BusinessProcesses.Runs;

public sealed class GetCurrentRunProcessor(IQueryDispatcher queries) : IProcessor<GetCurrentRunRequest, ScenarioRunView?>
{
    public Task<Result<ScenarioRunView?>> Process(GetCurrentRunRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetCurrentRunQuery(), cancellationToken);
    }
}
