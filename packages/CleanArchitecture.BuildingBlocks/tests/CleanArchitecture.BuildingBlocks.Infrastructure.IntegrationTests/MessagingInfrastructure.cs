using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using CleanArchitecture.BuildingBlocks.Infrastructure.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.IntegrationTests;

/// <summary>One PostgreSQL server and one Redis instance shared by every test; each test gets fresh databases and streams.</summary>
public sealed class MessagingInfrastructure : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:8.6").Build();

    public IConnectionMultiplexer Redis { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
        Redis = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await Redis.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }

    /// <summary>Creates an empty database, as each service owns its own (ADR-009).</summary>
    public async Task<string> CreateDatabaseAsync(string prefix)
    {
        string name = $"{prefix}_{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"create database {name}", connection);
        await command.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;
    }

    /// <summary>Starts a host that plays one service: its own database, the shared Redis and fast timings.</summary>
    public async Task<IHost> StartServiceAsync(
        string serviceName,
        string connectionString,
        Action<MessagingBuilder> declareMessages,
        Action<IServiceCollection>? configureServices = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        builder.Services.AddSingleton(Redis);

        MessagingBuilder messaging = builder.Services
            .AddMessaging(serviceName, outbox =>
            {
                outbox.PollInterval = TimeSpan.FromMilliseconds(50);
                outbox.InitialRetryDelay = TimeSpan.FromMilliseconds(100);
                outbox.MaxRetryDelay = TimeSpan.FromSeconds(1);
            })
            .AddPostgresStorage()
            .AddRedisStreams(streams =>
            {
                streams.PollInterval = TimeSpan.FromMilliseconds(50);
                streams.ClaimIdleTime = TimeSpan.FromMilliseconds(500);
                streams.MaxDeliveries = 3;
            });

        declareMessages(messaging);
        configureServices?.Invoke(builder.Services);

        IHost host = builder.Build();
        await host.StartAsync();
        return host;
    }

    public static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Polls until <paramref name="condition"/> holds, failing after <paramref name="timeout"/>.</summary>
    public static async Task EventuallyAsync(Func<Task<bool>> condition, string description, TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting until {description}.");
    }
}

[CollectionDefinition(Name)]
public sealed class MessagingCollection : ICollectionFixture<MessagingInfrastructure>
{
    public const string Name = "messaging";
}
