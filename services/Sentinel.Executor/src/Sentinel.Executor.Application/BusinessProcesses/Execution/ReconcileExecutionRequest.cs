using CleanArchitecture.BuildingBlocks.Messaging;

namespace Sentinel.Executor.Application.BusinessProcesses.Execution;

/// <summary>Reconciliation finishes one request that stopped part-way, for example because Executor was killed mid-execution.</summary>
public sealed record ReconcileExecutionRequest(string IdempotencyKey) : IRequest;
