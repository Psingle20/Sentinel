using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.Scenarios.Application.Abstractions;
using Reference.Scenarios.Application.Traffic;

namespace Reference.Scenarios.Application.BusinessTasks.Queries.Traffic;

public sealed class GetTrafficQueryHandler(ITraffic traffic) : IQueryHandler<GetTrafficQuery, TrafficSettings>
{
    public Task<Result<TrafficSettings>> Handle(GetTrafficQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(traffic.Settings));
}
