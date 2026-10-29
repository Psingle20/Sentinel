using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessTasks.Queries.Runs;
using Reference.Scenarios.Application.Runs;

namespace Reference.Scenarios.Application.BusinessProcesses.Runs;

public sealed class GetRunProcessor(IQueryDispatcher queries) : IProcessor<GetRunRequest, ScenarioRunView?>
{
    public Task<Result<ScenarioRunView?>> Process(GetRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetRunQuery(request.RunId), cancellationToken);
    }
}
