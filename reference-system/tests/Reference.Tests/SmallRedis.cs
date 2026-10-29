using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Shouldly;
using Testcontainers.Redis;
using Reference.Checkout.Redis;
using Reference.Checkout.Revisions;
using Reference.ReportingWorker.Batch;
using Reference.ReportingWorker.Revisions;
using Reference.RedisExporter.Sampling;

namespace Reference.Tests;

/// <summary>A real Redis with a small maxclients, so saturation happens in milliseconds.</summary>
public sealed class SmallRedis : IAsyncLifetime
{
    public const int MaxClients = 30;

    private readonly RedisContainer _container = new RedisBuilder("redis:8.6")
        .WithCommand("redis-server", "--maxclients", MaxClients.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .Build();

    public RedisEndpoint Endpoint { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Endpoint = RedisEndpoint.Parse(_container.GetConnectionString());
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Connected clients per name, as the exporter sees them.</summary>
    public async Task<Dictionary<string, int>> ClientsAsync()
    {
        await using RespConnection connection = await RespConnection.OpenAsync(Endpoint, "test-probe", TimeSpan.FromSeconds(2), CancellationToken.None);
        string list = await connection.ExecuteAsync(TimeSpan.FromSeconds(2), CancellationToken.None, "CLIENT", "LIST") ?? string.Empty;
        return RedisClientSampler.CountByName(list);
    }

    public async Task EventuallyAsync(string client, Func<int, bool> condition)
    {
        int count = 0;
        for (int attempt = 0; attempt < 100; attempt++)
        {
            count = (await ClientsAsync()).GetValueOrDefault(client);
            if (condition(count))
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new ShouldAssertException($"{client} has {count} connections, which never met the condition.");
    }
}
