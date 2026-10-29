using System.Diagnostics.Metrics;
using Reference.Common;
using Reference.Scenarios.Application.Abstractions;
using Reference.Scenarios.Application.Traffic;

namespace Reference.Scenarios.Api.Traffic;

/// <summary>
/// Continuous, seeded checkout traffic. Inter-request gaps vary by up to 10 percent from a fixed
/// seed, and in-flight requests are capped, so a stalled checkout cannot exhaust the machine.
/// </summary>
public sealed class TrafficGenerator(IHttpClientFactory clients, IConfiguration configuration, TimeProvider time, ILogger<TrafficGenerator> logger)
    : BackgroundService, ITraffic
{
    public const string SourceName = "Reference.Scenarios";
    public const int MaxInFlight = 50;

    private readonly Meter _meter = new(SourceName);
    private readonly SemaphoreSlim _inFlight = new(MaxInFlight, MaxInFlight);
    private readonly SeededJitter _jitter = new(configuration.GetValue("Reference:Seed", 1000));
    private TrafficSettings _settings = new(configuration.GetValue("Traffic:RequestsPerSecond", 10.0), false);
    private long _sequence;
    private Counter<long>? _sent;

    public TrafficSettings Settings => Volatile.Read(ref _settings);

    public void Update(TrafficSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        double rate = Math.Clamp(settings.RequestsPerSecond, 0.1, 50);
        Volatile.Write(ref _settings, settings with { RequestsPerSecond = rate });
        logger.LogInformation("Traffic set to {Rate} requests/s, paused {Paused}", rate, settings.Paused);
    }

    public override void Dispose()
    {
        _meter.Dispose();
        _inFlight.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _sent = _meter.CreateCounter<long>("reference.traffic.requests", "{request}", "Checkout requests sent by the traffic generator.");
        HttpClient http = clients.CreateClient(nameof(TrafficGenerator));
        Uri checkout = new(new Uri(configuration["services:checkout-service:http:0"]
            ?? throw new InvalidOperationException("The checkout-service address is not configured.")), "/api/checkout");

        while (!stoppingToken.IsCancellationRequested)
        {
            TrafficSettings settings = Settings;
            TimeSpan gap = _jitter.Vary(TimeSpan.FromSeconds(1 / settings.RequestsPerSecond), 0.1);
            if (!settings.Paused)
            {
                if (await _inFlight.WaitAsync(0, stoppingToken))
                {
                    _ = SendAsync(http, checkout, stoppingToken);
                }
                else
                {
                    _sent.Add(1, new KeyValuePair<string, object?>("status", "dropped"));
                }
            }

            await Task.Delay(gap, time, stoppingToken);
        }
    }

    private async Task SendAsync(HttpClient http, Uri checkout, CancellationToken cancellationToken)
    {
        string status;
        try
        {
            long sequence = Interlocked.Increment(ref _sequence);
            using HttpResponseMessage response = await http.PostAsJsonAsync(checkout, new { cartId = $"cart-{sequence}" }, cancellationToken);
            status = ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            status = "unreachable";
        }
        finally
        {
            _inFlight.Release();
        }

        _sent?.Add(1, new KeyValuePair<string, object?>("status", status));
    }
}
