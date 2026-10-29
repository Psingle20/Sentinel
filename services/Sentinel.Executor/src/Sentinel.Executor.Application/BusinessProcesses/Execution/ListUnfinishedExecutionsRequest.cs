using CleanArchitecture.BuildingBlocks.Messaging;

namespace Sentinel.Executor.Application.BusinessProcesses.Execution;

/// <summary>The idempotency keys of requests that stopped part-way and have waited long enough to reconcile.</summary>
public sealed record ListUnfinishedExecutionsRequest : IRequest<IReadOnlyList<string>>;
