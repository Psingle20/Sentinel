using System.Collections.Concurrent;

namespace Reference.Scenarios.Infrastructure.Environments.Aws;

/// <summary>
/// What provisioning created during this runner's lifetime, shared by every scope: the task definition of each consumer
/// revision. Provisioning runs once, behind the gate.
/// </summary>
public sealed class AwsProvisioning : IDisposable
{
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public ConcurrentDictionary<string, string> TaskDefinitions { get; } = new(StringComparer.Ordinal);

    public bool Done { get; set; }

    public void Dispose() => Gate.Dispose();
}
