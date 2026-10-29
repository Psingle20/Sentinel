using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;
using Sentinel.Gateway.Application.ControlPlane;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;

/// <summary>The owning service's answer for a known area; an unknown area reaches no service.</summary>
public sealed class GetConfigurationQueryHandler(IConfigurationOwners owners) : IQueryHandler<GetConfigurationQuery, ControlPlaneResponse>
{
    public async Task<Result<ControlPlaneResponse>> Handle(GetConfigurationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(ConfigurationAreas.All.Contains(query.Area) ? await owners.CurrentAsync(query.Area, cancellationToken) : ConfigurationAreas.Unknown);
    }
}
