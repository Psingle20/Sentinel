using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Logging;
using Reference.DeployController.Application.Options;
using Reference.DeployController.Domain.Revisions;
using Reference.DeployController.Domain.Revisions.Events;

namespace Reference.DeployController.Application.Services;

/// <summary>
/// The one ledger, kept in memory for the controller's lifetime and used by one caller at a time. What
/// the ledger records is logged from the events it raises.
/// </summary>
public sealed class LedgerState(DeployControllerOptions options, ILogger<LedgerState> logger)
{
    private readonly Lock _gate = new();
    private readonly DeploymentLedger _ledger = new(options.Services.Select(service => new ManagedRevisions(service.Key, service.Value.Baseline, service.Value.Revisions)));

    public TResult Use<TResult>(Func<DeploymentLedger, TResult> use)
    {
        ArgumentNullException.ThrowIfNull(use);

        IReadOnlyList<IDomainEvent> raised;
        TResult result;
        lock (_gate)
        {
            result = use(_ledger);
            raised = _ledger.DequeueDomainEvents();
        }

        foreach (IDomainEvent raisedEvent in raised)
        {
            Log(raisedEvent);
        }

        return result;
    }

    private void Log(IDomainEvent raisedEvent)
    {
        switch (raisedEvent)
        {
            case DeploymentStarted { Deployment: var started }:
                logger.LogInformation("{Kind} of {Service} {Version} (from {Previous}) started by {Actor}", started.Kind, started.Service, started.Version, started.PreviousVersion, started.Actor);
                break;
            case DeploymentFinished { Deployment: { Status: DeploymentStatus.Completed } completed }:
                logger.LogInformation("{Kind} of {Service} {Version} completed", completed.Kind, completed.Service, completed.Version);
                break;
            case ServiceReset { Deployment: var reset }:
                logger.LogInformation("{Service} reset from {Previous} to {Version}", reset.Service, reset.PreviousVersion, reset.Version);
                break;
        }
    }
}
