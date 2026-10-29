using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.ControlPlane;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;

public sealed class ListConfigurationAreasQueryHandler : IQueryHandler<ListConfigurationAreasQuery, IReadOnlyList<string>>
{
    public Task<Result<IReadOnlyList<string>>> Handle(ListConfigurationAreasQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(ConfigurationAreas.All));
}
