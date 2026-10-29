using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Domain.Executions;

namespace Sentinel.Executor.Application.BusinessProcesses.Executions;

/// <summary>The execution audit trail, newest first.</summary>
public sealed record ListExecutionsRequest(int Limit) : IRequest<IReadOnlyList<ExecutionSnapshot>>;
