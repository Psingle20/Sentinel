using Npgsql;
using Sentinel.Runtime.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Sentinel.Runtime.Tests;

/// <summary>One PostgreSQL container with Runtime's schema for a test class; tests keep apart by using their own tenants.</summary>
public sealed class RuntimePostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        DataSource = NpgsqlDataSource.Create(_postgres.GetConnectionString());
        await new RuntimeSchema(DataSource).InitializeAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
