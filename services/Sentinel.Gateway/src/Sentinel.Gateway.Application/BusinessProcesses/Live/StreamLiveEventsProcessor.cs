using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Live;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Live;

namespace Sentinel.Gateway.Application.BusinessProcesses.Live;

public sealed class StreamLiveEventsProcessor(IQueryDispatcher queries) : IProcessor<StreamLiveEventsRequest, IAsyncEnumerable<LiveEvent>>
{
    public Task<Result<IAsyncEnumerable<LiveEvent>>> Process(StreamLiveEventsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new StreamLiveEventsQuery(request.Tenants, request.After), cancellationToken);
    }
}
