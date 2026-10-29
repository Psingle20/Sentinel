using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessProcesses.Scenarios;
using Reference.Scenarios.Domain.Scenarios;

namespace Reference.Scenarios.Application.BusinessTasks.Queries.Scenarios;

public sealed class ListScenariosQueryHandler(ScenarioCatalog catalog) : IQueryHandler<ListScenariosQuery, IReadOnlyList<ScenarioSummary>>
{
    public Task<Result<IReadOnlyList<ScenarioSummary>>> Handle(ListScenariosQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<IReadOnlyList<ScenarioSummary>>([.. catalog.Manifests.Values
            .OrderBy(manifest => manifest.Id, StringComparer.Ordinal)
            .Select(manifest => new ScenarioSummary(manifest.Id, manifest.Slug, manifest.Kind, manifest.Version, manifest.Description))]));
}
