using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.IntegrationTests;

/// <summary>Controls how many deliveries fail before handling succeeds, and how long each handling takes.</summary>
public sealed class BillingBehavior
{
    private int _attempts;
    private int _inFlight;
    private int _mostInFlight;

    public int FailFirst { get; init; }

    public TimeSpan HandlingTime { get; init; }

    public int Attempts => _attempts;

    /// <summary>The most deliveries that were being handled at the same moment.</summary>
    public int MostInFlight => Volatile.Read(ref _mostInFlight);

    internal bool NextAttemptFails() => Interlocked.Increment(ref _attempts) <= FailFirst;

    internal async Task HandleAsync(CancellationToken cancellationToken)
    {
        int now = Interlocked.Increment(ref _inFlight);
        int seen;
        do
        {
            seen = Volatile.Read(ref _mostInFlight);
        }
        while (now > seen && Interlocked.CompareExchange(ref _mostInFlight, now, seen) != seen);

        try
        {
            if (HandlingTime > TimeSpan.Zero)
            {
                await Task.Delay(HandlingTime, cancellationToken);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}
