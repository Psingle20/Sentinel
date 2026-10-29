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

/// <summary>The only writer of one incident's stream (ADR-004), keyed by the incident ID.</summary>
public interface IIncidentGrain : IGrainWithGuidKey
{
    Task<CommandOutcome> Send(IncidentCommand command);

    Task<IncidentSnapshot?> Get();
}
