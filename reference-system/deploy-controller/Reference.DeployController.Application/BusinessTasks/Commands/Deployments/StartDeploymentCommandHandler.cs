using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.Abstractions;
using Reference.DeployController.Application.BusinessProcesses.Deployments;
using Reference.DeployController.Application.Options;
using Reference.DeployController.Application.Services;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessTasks.Commands.Deployments;

/// <summary>
/// Starts a deployment from the revision the service serves now. A key already used gets its original
/// deployment back; a rollout is completed by the scheduler once it has run for its rollout time.
/// </summary>
public sealed class StartDeploymentCommandHandler(
    LedgerState ledger,
    IRevisionSwitch revisions,
    IRolloutScheduler rollouts,
    DeployControllerOptions options,
    TimeProvider time) : ICommandHandler<StartDeploymentCommand, DeployResult>
{
    public async Task<Result<DeployResult>> Handle(StartDeploymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (ledger.Use(state => state.Replay(command.IdempotencyKey)) is { } replayed)
        {
            return Result.Success(new DeployResult(DeployOutcome.Replayed, replayed));
        }

        if (ledger.Use(state => state.Service(command.Service)) is not { } managed)
        {
            return Result.Success(new DeployResult(DeployOutcome.UnknownService, null));
        }

        string serving = await revisions.ServingAsync(command.Service, cancellationToken) ?? managed.Baseline;
        DeployResult result = ledger.Use(state =>
        {
            if (state.Replay(command.IdempotencyKey) is { } again)
            {
                return new DeployResult(DeployOutcome.Replayed, again);
            }

            Result<DeploymentRecord> started = state.Start(
                command.Service, command.Kind, command.Version, serving, command.Actor ?? "unknown", command.Reason, command.ConfigurationDiff, command.IdempotencyKey, time.GetUtcNow());
            if (started.IsSuccess)
            {
                return new DeployResult(DeployOutcome.Accepted, started.Value);
            }

            return started.Error == DeploymentErrors.Conflict
                ? new DeployResult(DeployOutcome.Conflict, state.Active(command.Service))
                : new DeployResult(DeployOutcome.UnknownRevision, null);
        });

        if (result is { Outcome: DeployOutcome.Accepted, Deployment: { Status: DeploymentStatus.InProgress } rolling })
        {
            rollouts.Schedule(rolling.DeploymentId, TimeSpan.FromSeconds(command.RolloutSeconds ?? options.RolloutSeconds));
        }

        return Result.Success(result);
    }
}
