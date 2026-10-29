using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Application.Services;

namespace Sentinel.Executor.Application.BusinessTasks.Commands.Execution;

/// <summary>
/// Finishes a request that stopped part-way, from the state it reached; the target is inspected
/// instead of repeating the action. A request that reached a final outcome meanwhile, or that this
/// process is still handling, is left alone.
/// </summary>
public sealed class ReconcileExecutionCommandHandler(IExecutionStore store, ExecutionService executions, InFlightExecutions inFlight, ILogger<ReconcileExecutionCommandHandler> logger) : ICommandHandler<ReconcileExecutionCommand>
{
    public async Task<Result> Handle(ReconcileExecutionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (inFlight.Contains(command.IdempotencyKey))
        {
            return Result.Success();
        }

        if (await store.FindAsync(command.IdempotencyKey, cancellationToken) is { HasFinalOutcome: false } request)
        {
            logger.LogWarning("Reconciling unfinished execution {Request}", ExecutionService.Describe(request));
            await executions.ReconcileAsync(request, cancellationToken);
        }

        return Result.Success();
    }
}
