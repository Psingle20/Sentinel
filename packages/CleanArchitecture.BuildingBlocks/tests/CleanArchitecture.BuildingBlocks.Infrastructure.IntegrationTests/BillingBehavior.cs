using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Npgsql;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.IntegrationTests;

/// <summary>Controls how many deliveries fail before handling succeeds.</summary>
public sealed class BillingBehavior
{
    private int _attempts;

    public int FailFirst { get; init; }

    public int Attempts => _attempts;

    internal bool NextAttemptFails() => Interlocked.Increment(ref _attempts) <= FailFirst;
}
