using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;

public static class PostgresDataSourceExtensions
{
    public const string HealthCheckName = "postgres";

    /// <summary>
    /// Registers one <see cref="NpgsqlDataSource"/> for the service's own database and a health
    /// check that runs <c>select 1</c>, tagged with <paramref name="healthCheckTags"/>.
    /// </summary>
    public static IServiceCollection AddPostgresDataSource(this IServiceCollection services, string connectionString, params string[] healthCheckTags)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddHealthChecks().Add(new HealthCheckRegistration(
            HealthCheckName,
            provider => new PostgresHealthCheck(provider.GetRequiredService<NpgsqlDataSource>()),
            HealthStatus.Unhealthy,
            healthCheckTags));
        return services;
    }

    private sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await using NpgsqlCommand command = dataSource.CreateCommand("select 1");
                await command.ExecuteScalarAsync(cancellationToken);
                return HealthCheckResult.Healthy();
            }
            catch (NpgsqlException exception)
            {
                return HealthCheckResult.Unhealthy("PostgreSQL is unreachable.", exception);
            }
        }
    }
}
