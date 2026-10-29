using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessTasks.Queries.Scenarios;
using Reference.Scenarios.Domain.Scenarios;

namespace Reference.Scenarios.Application.BusinessProcesses.Scenarios;

public sealed class GetScenarioProcessor(IQueryDispatcher queries) : IProcessor<GetScenarioRequest, ScenarioManifest?>
{
    public Task<Result<ScenarioManifest?>> Process(GetScenarioRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new GetScenarioQuery(request.ScenarioId), cancellationToken);
    }
}
