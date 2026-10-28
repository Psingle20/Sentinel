using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Redis;

public static class RedisConnectionExtensions
{
    public const string HealthCheckName = "redis";

    /// <summary>
    /// Registers one <see cref="IConnectionMultiplexer"/> for the messaging Redis, connected on
    /// first use and reconnecting in the background, and a PING health check tagged with
    /// <paramref name="healthCheckTags"/>.
    /// </summary>
    public static IServiceCollection AddRedisConnection(this IServiceCollection services, string connectionString, params string[] healthCheckTags)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddHealthChecks().Add(new HealthCheckRegistration(
            HealthCheckName,
            provider => new RedisHealthCheck(provider.GetRequiredService<IConnectionMultiplexer>()),
            HealthStatus.Unhealthy,
            healthCheckTags));
        return services;
    }

    private sealed class RedisHealthCheck(IConnectionMultiplexer redis) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                TimeSpan latency = await redis.GetDatabase().PingAsync();
                return HealthCheckResult.Healthy($"PING {latency.TotalMilliseconds:F0} ms");
            }
            catch (RedisException exception)
            {
                return HealthCheckResult.Unhealthy("Redis is unreachable.", exception);
            }
        }
    }
}
