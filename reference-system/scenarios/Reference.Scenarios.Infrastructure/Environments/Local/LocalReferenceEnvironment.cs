using Reference.Scenarios.Application.Abstractions;
using Reference.Scenarios.Domain.Scenarios;

namespace Reference.Scenarios.Infrastructure.Environments.Local;

/// <summary>
/// The local reference environment the AppHost starts and provisions: every step goes to the deploy controller, as a CI
/// pipeline would use it, or to a workload's guarded fault control, through the reference client.
/// </summary>
public sealed class LocalReferenceEnvironment(IReferenceControl control) : IReferenceEnvironment
{
    public const string EnvironmentName = "local";

    public string Name => EnvironmentName;

    public Task ProvisionAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ResetAsync(CancellationToken cancellationToken) => control.ResetAsync(cancellationToken);

    public Task ActivateAsync(ScenarioStep step, string runId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);

        return step.Action switch
        {
            "deploy" or "configChange" => control.DeployAsync(step, runId, cancellationToken),
            "startBatch" => control.StartBatchAsync(step, cancellationToken),
            "stopBatch" => control.StopBatchAsync(cancellationToken),
            "poisonMessage" => control.InjectPoisonMessageAsync(step, cancellationToken),
            "duplicatePublication" => control.DuplicatePublicationAsync(step, cancellationToken),
            "slowConfirmations" => control.SlowConfirmationsAsync(step, cancellationToken),
            "plantLog" => control.PlantLogAsync(step, cancellationToken),
            _ => throw new ReferenceEnvironmentException(EnvironmentFailure.Unsupported, $"Unknown action {step.Action}.")
        };
    }

    public Task<EnvironmentState> InspectAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new EnvironmentState(Name, Ready: true, Problems: []));
}
