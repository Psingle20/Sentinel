using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using Reference.Common.Redis;

namespace Reference.RedisExporter.Sampling;

/// <summary>
/// Samples <c>CLIENT LIST</c> every two seconds and exports connections per client name, the
/// signal that separates S1 (checkout's own connections grow) from S2 (reporting-worker's do).
/// Names seen once keep reporting zero, so a series never silently disappears.
/// </summary>
public sealed class RedisClientSampler(RedisEndpoint endpoint, TimeProvider time, ILogger<RedisClientSampler> logger) : BackgroundService
{
    public const string SourceName = "Reference.RedisExporter";
    public const string ClientName = "redis-exporter";

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<string, int> _byName = new(StringComparer.Ordinal);
    private int _connected;
    private int _maxClients;

    public IReadOnlyDictionary<string, int> ClientsByName => _byName;

    public int ConnectedClients => _connected;

    public int MaxClients => _maxClients;

    /// <summary>Counts <c>CLIENT LIST</c> lines per <c>name=</c> value; unnamed clients count as <c>unnamed</c>.</summary>
    public static Dictionary<string, int> CountByName(string clientList)
    {
        ArgumentNullException.ThrowIfNull(clientList);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string line in clientList.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string name = line.Split(' ')
                .Where(field => field.StartsWith("name=", StringComparison.Ordinal))
                .Select(field => field["name=".Length..])
                .FirstOrDefault(value => value.Length > 0) ?? "unnamed";
            counts[name] = counts.GetValueOrDefault(name) + 1;
        }

        return counts;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        RespConnection? connection = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                connection ??= await RespConnection.OpenAsync(endpoint, ClientName, Timeout, stoppingToken);
                await SampleAsync(connection, stoppingToken);
            }
            catch (RedisUnavailableException exception)
            {
                logger.LogWarning(exception, "Sampling Redis clients failed: {Reason}", exception.InnerException?.Message ?? exception.Message);
                if (connection is not null)
                {
                    await connection.DisposeAsync();
                    connection = null;
                }
            }

            await Task.Delay(Interval, time, stoppingToken);
        }

        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
    }

    private async Task SampleAsync(RespConnection connection, CancellationToken cancellationToken)
    {
        string list = await connection.ExecuteAsync(Timeout, cancellationToken, "CLIENT", "LIST") ?? string.Empty;
        Dictionary<string, int> counts = CountByName(list);

        foreach (string known in _byName.Keys)
        {
            _byName[known] = counts.GetValueOrDefault(known);
        }

        foreach ((string name, int count) in counts)
        {
            _byName[name] = count;
        }

        _connected = counts.Values.Sum();

        if (_maxClients == 0)
        {
            string reply = await connection.ExecuteAsync(Timeout, cancellationToken, "CONFIG", "GET", "maxclients") ?? string.Empty;
            string[] parts = reply.Split('\n');
            if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int maxClients))
            {
                _maxClients = maxClients;
            }
        }
    }
}
