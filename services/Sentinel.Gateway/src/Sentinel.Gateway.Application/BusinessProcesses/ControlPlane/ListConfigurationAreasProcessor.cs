using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

public sealed class ListConfigurationAreasProcessor(IQueryDispatcher queries) : IProcessor<ListConfigurationAreasRequest, IReadOnlyList<string>>
{
    public Task<Result<IReadOnlyList<string>>> Process(ListConfigurationAreasRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new ListConfigurationAreasQuery(), cancellationToken);
    }
}
