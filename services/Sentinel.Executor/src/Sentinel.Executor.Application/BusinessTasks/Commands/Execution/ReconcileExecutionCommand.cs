using CleanArchitecture.BuildingBlocks.Messaging;

namespace Sentinel.Executor.Application.BusinessTasks.Commands.Execution;

public sealed record ReconcileExecutionCommand(string IdempotencyKey) : ICommand;
