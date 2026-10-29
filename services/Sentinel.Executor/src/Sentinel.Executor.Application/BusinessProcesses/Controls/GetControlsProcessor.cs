using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.BusinessTasks.Queries.Controls;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.BusinessProcesses.Controls;

public sealed class GetControlsProcessor(IQueryDispatcher queries) : IProcessor<GetControlsRequest, ExecutorControls>
{
    public Task<Result<ExecutorControls>> Process(GetControlsRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetControlsQuery(), cancellationToken);
    }
}
