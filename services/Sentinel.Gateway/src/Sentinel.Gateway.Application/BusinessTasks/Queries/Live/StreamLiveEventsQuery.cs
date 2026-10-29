using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Gateway.Application.Live;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Live;

public sealed record StreamLiveEventsQuery(IReadOnlyList<TenantId> Tenants, long? After) : IQuery<IAsyncEnumerable<LiveEvent>>;
