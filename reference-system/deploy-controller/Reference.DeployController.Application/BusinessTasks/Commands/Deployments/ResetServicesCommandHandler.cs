using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.Abstractions;
using Reference.DeployController.Application.Services;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessTasks.Commands.Deployments;

/// <summary>Clears every injected fault and returns every service to its baseline at once; services already there are untouched.</summary>
public sealed class ResetServicesCommandHandler(LedgerState ledger, DeploymentFaults faults, IRevisionSwitch revisions, TimeProvider time) : ICommandHandler<ResetServicesCommand, IReadOnlyList<DeploymentRecord>>
{
    public async Task<Result<IReadOnlyList<DeploymentRecord>>> Handle(ResetServicesCommand command, CancellationToken cancellationToken)
    {
        faults.FailRollbacks = false;
        var resets = new List<DeploymentRecord>();
        foreach (ManagedRevisions managed in ledger.Use(state => state.Services))
        {
            string? serving = await revisions.ServingAsync(managed.Service, cancellationToken);
            if (serving == managed.Baseline)
            {
                continue;
            }

            await revisions.SwitchAsync(managed.Service, managed.Baseline, cancellationToken);
            if (ledger.Use(state => state.Reset(managed.Service, serving, time.GetUtcNow())) is { } reset)
            {
                resets.Add(reset);
            }
        }

        return Result.Success<IReadOnlyList<DeploymentRecord>>(resets);
    }
}
