using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.Services;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessTasks.Queries.Deployments;

public sealed class ListDeploymentsQueryHandler(LedgerState ledger) : IQueryHandler<ListDeploymentsQuery, IReadOnlyList<DeploymentRecord>>
{
    public Task<Result<IReadOnlyList<DeploymentRecord>>> Handle(ListDeploymentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult(Result.Success(ledger.Use(state => state.Query(query.Since, query.Until, query.Service))));
    }
}
