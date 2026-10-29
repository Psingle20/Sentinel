using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Executor.Domain.Executions;

public sealed record ExecutionTransition(ExecutionState From, ExecutionState To, string? Detail, DateTimeOffset At);
