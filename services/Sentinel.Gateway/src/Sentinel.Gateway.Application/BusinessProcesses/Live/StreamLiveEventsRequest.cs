using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Live;

namespace Sentinel.Gateway.Application.BusinessProcesses.Live;

/// <summary>The live events of the caller's tenants after a sequence, then new ones as they are recorded.</summary>
public sealed record StreamLiveEventsRequest(IReadOnlyList<TenantId> Tenants, long? After) : IRequest<IAsyncEnumerable<LiveEvent>>;
