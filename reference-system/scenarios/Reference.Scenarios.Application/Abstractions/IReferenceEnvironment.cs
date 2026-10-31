using Reference.Scenarios.Domain.Scenarios;

namespace Reference.Scenarios.Application.Abstractions;

/// <summary>
/// The reference environment a runner drives, in lifecycle terms rather than any provider's API (Phase E01): the local
/// one the AppHost starts, or the AWS one the runner provisions itself. Reset is idempotent, and an operation the
/// environment cannot perform, a timeout or a reset that leaves something behind throws a
/// <see cref="ReferenceEnvironmentException"/> saying which.
/// </summary>
public interface IReferenceEnvironment
{
    /// <summary><c>local</c> or <c>aws</c>, as manifests name it.</summary>
    string Name { get; }

    /// <summary>Creates whatever the environment needs and does not have yet; repeating it changes nothing.</summary>
    Task ProvisionAsync(CancellationToken cancellationToken);

    /// <summary>Returns the environment to its healthy baseline and proves nothing of an earlier run remains.</summary>
    Task ResetAsync(CancellationToken cancellationToken);

    /// <summary>Carries out one step of a scenario's timeline.</summary>
    Task ActivateAsync(ScenarioStep step, string runId, CancellationToken cancellationToken);

    Task<EnvironmentState> InspectAsync(CancellationToken cancellationToken);
}
