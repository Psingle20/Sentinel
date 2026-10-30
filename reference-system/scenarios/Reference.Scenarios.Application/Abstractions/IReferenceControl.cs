using Reference.Scenarios.Domain.Scenarios;

namespace Reference.Scenarios.Application.Abstractions;

/// <summary>The reference system as the runner drives it: the deploy controller, as a CI pipeline would use it, the reporting-worker's batch and the queue-consumer's queue.</summary>
public interface IReferenceControl
{
    Task ResetAsync(CancellationToken cancellationToken);

    Task DeployAsync(ScenarioStep step, string runId, CancellationToken cancellationToken);

    Task StartBatchAsync(ScenarioStep step, CancellationToken cancellationToken);

    Task StopBatchAsync(CancellationToken cancellationToken);

    /// <summary>Publishes one message to the step's service's queue that no revision can process.</summary>
    Task InjectPoisonMessageAsync(ScenarioStep step, CancellationToken cancellationToken);
}
