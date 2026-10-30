using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Abstractions;
using Reference.Scenarios.Application.BusinessTasks.Queries.Environments;

namespace Reference.Scenarios.Application.BusinessProcesses.Environments;

public sealed class GetEnvironmentProcessor(IQueryDispatcher queries) : IProcessor<GetEnvironmentRequest, EnvironmentState>
{
    public Task<Result<EnvironmentState>> Process(GetEnvironmentRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetEnvironmentQuery(), cancellationToken);
    }
}
