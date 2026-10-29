using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Domain.Scenarios;

namespace Reference.Scenarios.Application.BusinessTasks.Queries.Scenarios;

public sealed class GetScenarioQueryHandler(ScenarioCatalog catalog) : IQueryHandler<GetScenarioQuery, ScenarioManifest?>
{
    public Task<Result<ScenarioManifest?>> Handle(GetScenarioQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult(Result.Success(catalog.Manifests.GetValueOrDefault(query.ScenarioId)));
    }
}
