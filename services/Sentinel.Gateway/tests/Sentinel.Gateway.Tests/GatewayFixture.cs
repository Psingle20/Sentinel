using Testcontainers.PostgreSql;

namespace Sentinel.Gateway.Tests;

/// <summary>One PostgreSQL container and one Gateway host for a test class; tests keep apart by using their own incidents and idempotency keys.</summary>
public sealed class GatewayFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();

    public GatewayHost Host { get; private set; } = null!;

    public HttpClient Client { get; private set; } = null!;

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        Host = new GatewayHost(_postgres.GetConnectionString());
        await Host.InitializeStoresAsync();
        Client = Host.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Host.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
