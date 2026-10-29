using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using Reference.Common.Redis;
using Reference.RedisExporter.Sampling;

namespace Reference.RedisExporter.Telemetry;

public sealed class RedisExporterTelemetry : IDisposable
{
    private readonly Meter _meter = new(RedisClientSampler.SourceName);

    public RedisExporterTelemetry(RedisClientSampler sampler)
    {
        ArgumentNullException.ThrowIfNull(sampler);

        _meter.CreateObservableGauge(
            "reference.redis.clients",
            () => sampler.ClientsByName.Select(pair => new Measurement<int>(pair.Value, new KeyValuePair<string, object?>("client.name", pair.Key))),
            "{connection}",
            "Connected Redis clients by client name.");
        _meter.CreateObservableGauge("reference.redis.connected_clients", () => sampler.ConnectedClients, "{connection}", "All connected Redis clients.");
        _meter.CreateObservableGauge("reference.redis.maxclients", () => sampler.MaxClients, "{connection}", "Redis maxclients setting.");
    }

    public void Dispose() => _meter.Dispose();
}
