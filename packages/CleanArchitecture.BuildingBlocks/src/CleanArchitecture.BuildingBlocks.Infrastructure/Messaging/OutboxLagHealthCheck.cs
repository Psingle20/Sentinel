using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Degraded when unpublished outbox records are older than the configured threshold.</summary>
internal sealed class OutboxLagHealthCheck(IOutboxStore store, IOptions<OutboxOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        TimeSpan? age = await store.GetOldestPendingAgeAsync(cancellationToken);
        TimeSpan threshold = options.Value.LagThreshold;

        return age is { } lag && lag > threshold
            ? HealthCheckResult.Degraded($"Oldest unpublished outbox message is {lag.TotalSeconds:F0}s old (threshold {threshold.TotalSeconds:F0}s).")
            : HealthCheckResult.Healthy();
    }
}
