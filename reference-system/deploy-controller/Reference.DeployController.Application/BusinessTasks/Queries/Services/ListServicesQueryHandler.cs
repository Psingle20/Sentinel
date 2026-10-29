using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.BusinessProcesses.Services;
using Reference.DeployController.Application.Services;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessTasks.Queries.Services;

public sealed class ListServicesQueryHandler(LedgerState ledger, ServiceDescriber describer) : IQueryHandler<ListServicesQuery, IReadOnlyList<ServiceView>>
{
    public async Task<Result<IReadOnlyList<ServiceView>>> Handle(ListServicesQuery query, CancellationToken cancellationToken)
    {
        var views = new List<ServiceView>();
        foreach (ManagedRevisions managed in ledger.Use(state => state.Services))
        {
            views.Add(await describer.DescribeAsync(managed, cancellationToken));
        }

        return Result.Success<IReadOnlyList<ServiceView>>(views);
    }
}
