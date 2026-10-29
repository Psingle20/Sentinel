using Reference.Scenarios.Domain.Scenarios;

namespace Reference.Scenarios.Application.Abstractions;

/// <summary>The reference system as the runner drives it: the deploy controller, as a CI pipeline would use it, and the reporting-worker's batch.</summary>
public interface IReferenceControl
{
    Task ResetAsync(CancellationToken cancellationToken);

    Task DeployAsync(ScenarioStep step, string runId, CancellationToken cancellationToken);

    Task StartBatchAsync(ScenarioStep step, CancellationToken cancellationToken);

    Task StopBatchAsync(CancellationToken cancellationToken);
}
