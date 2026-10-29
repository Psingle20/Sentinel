using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.BusinessTasks.Queries.Traffic;
using Reference.Scenarios.Application.Traffic;

namespace Reference.Scenarios.Application.BusinessProcesses.Traffic;

public sealed class GetTrafficProcessor(IQueryDispatcher queries) : IProcessor<GetTrafficRequest, TrafficSettings>
{
    public Task<Result<TrafficSettings>> Process(GetTrafficRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetTrafficQuery(), cancellationToken);
    }
}
