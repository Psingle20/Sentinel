using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Incidents.Commands;

namespace Sentinel.Runtime.Infrastructure.Grains;

/// <summary><see cref="IIncidentOwner"/> backed by IncidentGrain.</summary>
internal sealed class GrainIncidentOwner(IGrainFactory grains) : IIncidentOwner
{
    public async Task<Result<CommandReply>> SendAsync(IncidentId incidentId, IncidentCommand command, CancellationToken cancellationToken) =>
        (await grains.GetGrain<IIncidentGrain>(incidentId.Value).Send(command)).ToResult();

    public Task<IncidentSnapshot?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        grains.GetGrain<IIncidentGrain>(incidentId.Value).Get();
}
