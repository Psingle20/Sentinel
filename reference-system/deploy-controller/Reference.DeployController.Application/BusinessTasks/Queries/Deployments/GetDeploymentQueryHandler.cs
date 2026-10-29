using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.Services;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessTasks.Queries.Deployments;

public sealed class GetDeploymentQueryHandler(LedgerState ledger) : IQueryHandler<GetDeploymentQuery, DeploymentRecord?>
{
    public Task<Result<DeploymentRecord?>> Handle(GetDeploymentQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Task.FromResult(Result.Success(ledger.Use(state => state.Find(query.DeploymentId))));
    }
}
