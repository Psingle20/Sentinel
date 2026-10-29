using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Reference.DeployController.Application.Abstractions;
using Reference.DeployController.Application.Services;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessTasks.Commands.Deployments;

/// <summary>Switches the service to the deployed revision and records the rollout completed, or records why it failed.</summary>
public sealed class CompleteRolloutCommandHandler(
    LedgerState ledger,
    DeploymentFaults faults,
    IRevisionSwitch revisions,
    TimeProvider time,
    ILogger<CompleteRolloutCommandHandler> logger) : ICommandHandler<CompleteRolloutCommand>
{
    public async Task<Result> Handle(CompleteRolloutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (ledger.Use(state => state.Find(command.DeploymentId)) is not { Status: DeploymentStatus.InProgress } record)
        {
            return Result.Success();
        }

        try
        {
            if (record.Kind == DeploymentKind.Rollback && faults.FailRollbacks)
            {
                throw new InvalidOperationException($"Injected fault: the rollback of {record.Service} to {record.Version} did not complete.");
            }

            await revisions.SwitchAsync(record.Service, record.Version, cancellationToken);
            ledger.Use(state => state.Finish(record.DeploymentId, null, time.GetUtcNow()));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            ledger.Use(state => state.Finish(record.DeploymentId, exception.Message, time.GetUtcNow()));
            logger.LogError(exception, "{Kind} of {Service} {Version} failed", record.Kind, record.Service, record.Version);
        }

        return Result.Success();
    }
}
