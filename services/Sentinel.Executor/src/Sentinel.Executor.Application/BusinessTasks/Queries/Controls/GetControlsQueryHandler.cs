using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.BusinessTasks.Queries.Controls;

public sealed class GetControlsQueryHandler(IControlStore controls) : IQueryHandler<GetControlsQuery, ExecutorControls>
{
    public async Task<Result<ExecutorControls>> Handle(GetControlsQuery query, CancellationToken cancellationToken) =>
        Result.Success(await controls.GetAsync(cancellationToken));
}
