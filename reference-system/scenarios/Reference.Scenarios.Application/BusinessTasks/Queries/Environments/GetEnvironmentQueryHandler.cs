using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Abstractions;

namespace Reference.Scenarios.Application.BusinessTasks.Queries.Environments;

public sealed class GetEnvironmentQueryHandler(IReferenceEnvironment environment) : IQueryHandler<GetEnvironmentQuery, EnvironmentState>
{
    public async Task<Result<EnvironmentState>> Handle(GetEnvironmentQuery query, CancellationToken cancellationToken) =>
        Result.Success(await environment.InspectAsync(cancellationToken));
}
