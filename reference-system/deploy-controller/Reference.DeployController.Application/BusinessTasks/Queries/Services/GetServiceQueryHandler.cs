using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.BusinessProcesses.Services;
using Reference.DeployController.Application.Services;

namespace Reference.DeployController.Application.BusinessTasks.Queries.Services;

public sealed class GetServiceQueryHandler(LedgerState ledger, ServiceDescriber describer) : IQueryHandler<GetServiceQuery, ServiceView?>
{
    public async Task<Result<ServiceView?>> Handle(GetServiceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return ledger.Use(state => state.Service(query.Name)) is { } managed
            ? Result.Success<ServiceView?>(await describer.DescribeAsync(managed, cancellationToken))
            : Result.Success<ServiceView?>(null);
    }
}
