using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Temporalio.Client;

namespace Sentinel.Runtime.Tests;

/// <summary>
/// A real Temporal server for the ADR-005 acceptance tests: the image the AppHost runs, backed by
/// PostgreSQL on a private network, on a fixed host port so the server can be restarted under the
/// same address. Images are the ones local orchestration already uses.
/// </summary>
public sealed class TemporalFixture : IAsyncLifetime
{
    public const string PostgresImage = "postgres:17";
    public const string TemporalImage = "temporalio/auto-setup:1.28.1";

    // Generated per run for a throwaway test database; never stored.
    private readonly string _databasePassword = Guid.NewGuid().ToString("N");

    private INetwork _network = null!;

    public IContainer Postgres { get; private set; } = null!;

    public IContainer Temporal { get; private set; } = null!;

    public string Address { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _network = new NetworkBuilder().WithName($"sentinel-temporal-{Guid.NewGuid():N}").Build();
        await _network.CreateAsync();
        Postgres = PostgresContainer("postgres");
        await Postgres.StartAsync();
        (Temporal, Address) = await StartTemporalAsync("postgres", FreePort());
    }

    public async Task DisposeAsync()
    {
        await Temporal.DisposeAsync();
        await Postgres.DisposeAsync();
        await _network.DisposeAsync();
    }

    /// <summary>A Postgres server on the fixture's network under <paramref name="alias"/>.</summary>
    public IContainer PostgresContainer(string alias) => new ContainerBuilder(PostgresImage)
        .WithNetwork(_network)
        .WithNetworkAliases(alias)
        .WithEnvironment("POSTGRES_PASSWORD", _databasePassword)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-U", "postgres"))
        .Build();

    /// <summary>Starts a Temporal server against the Postgres at <paramref name="postgresAlias"/> and waits until it serves.</summary>
    public async Task<(IContainer Container, string Address)> StartTemporalAsync(string postgresAlias, int hostPort)
    {
        IContainer temporal = new ContainerBuilder(TemporalImage)
            .WithNetwork(_network)
            .WithEnvironment("DB", "postgres12")
            .WithEnvironment("DB_PORT", "5432")
            .WithEnvironment("POSTGRES_SEEDS", postgresAlias)
            .WithEnvironment("POSTGRES_USER", "postgres")
            .WithEnvironment("POSTGRES_PWD", _databasePassword)
            .WithPortBinding(hostPort, 7233)
            .Build();
        await temporal.StartAsync();
        string address = $"localhost:{hostPort}";
        await WaitUntilServingAsync(address);
        return (temporal, address);
    }

    public static async Task WaitUntilServingAsync(string address)
    {
        DateTime deadline = DateTime.UtcNow.AddMinutes(3);
        while (true)
        {
            try
            {
                TemporalClient client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions(address));
                await client.WorkflowService.DescribeNamespaceAsync(new Temporalio.Api.WorkflowService.V1.DescribeNamespaceRequest { Namespace = "default" });
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

[CollectionDefinition(Name)]
public sealed class TemporalCollection : ICollectionFixture<TemporalFixture>
{
    public const string Name = "temporal";
}
