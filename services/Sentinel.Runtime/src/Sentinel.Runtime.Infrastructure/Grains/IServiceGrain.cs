using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;

namespace Sentinel.Runtime.Infrastructure.Grains;

/// <summary>Hot context for one service, keyed <c>tenant/environment/service</c>.</summary>
public interface IServiceGrain : IGrainWithStringKey
{
    Task<ServiceContext> Get();

    Task ObserveDependency(ServiceId callee);

    Task ObserveDependent(ServiceId caller);
}
