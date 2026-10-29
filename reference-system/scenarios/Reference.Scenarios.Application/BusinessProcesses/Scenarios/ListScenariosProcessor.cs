using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessTasks.Queries.Scenarios;

namespace Reference.Scenarios.Application.BusinessProcesses.Scenarios;

public sealed class ListScenariosProcessor(IQueryDispatcher queries) : IProcessor<ListScenariosRequest, IReadOnlyList<ScenarioSummary>>
{
    public Task<Result<IReadOnlyList<ScenarioSummary>>> Process(ListScenariosRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new ListScenariosQuery(), cancellationToken);
    }
}
