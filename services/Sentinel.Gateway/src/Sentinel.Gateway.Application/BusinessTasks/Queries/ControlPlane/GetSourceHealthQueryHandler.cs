using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;

/// <summary>Ingestion's answer, passed through with its status code and problem.</summary>
public sealed class GetSourceHealthQueryHandler(ISourceHealth sources) : IQueryHandler<GetSourceHealthQuery, ControlPlaneResponse>
{
    public async Task<Result<ControlPlaneResponse>> Handle(GetSourceHealthQuery query, CancellationToken cancellationToken) =>
        Result.Success(await sources.SourcesAsync(cancellationToken));
}
