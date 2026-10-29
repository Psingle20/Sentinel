using Sentinel.AgentWorker.Application.Capture;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Shouldly;

namespace Sentinel.AgentWorker.Tests;

/// <summary>Phase 07 test plan: captured inputs stay byte- and semantically stable, so stored executions keep replaying.</summary>
public sealed class CaptureStabilityTests
{
    // Captures already stored match their requests by this canonical form. If it changes, plan how stored
    // executions keep replaying before updating the pin (ADR-019).
    private const string PinnedQueryHash = "49ebd0dcb03d5fb0d48dddbc770a344222935db23d37d9d74d5e3e375ff9fb3c";

    private static readonly DateTimeOffset Start = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Equal_requests_hash_equally_whatever_order_their_filters_were_added_in()
    {
        ObservationQuery first = Query(("metricName", "redis_connections"), ("minimumSeverity", "warning"));
        ObservationQuery second = Query(("minimumSeverity", "warning"), ("metricName", "redis_connections"));

        InvestigationCapture.Hash(second).ShouldBe(InvestigationCapture.Hash(first));
    }

    [Fact]
    public void The_canonical_hash_of_a_request_is_pinned()
    {
        ObservationQuery query = Query(("metricName", "redis_connections"), ("minimumSeverity", "warning"));

        InvestigationCapture.Hash(query).ShouldBe(PinnedQueryHash, "a changed hash means stored captures no longer match their requests");
    }

    [Fact]
    public async Task A_capture_replays_from_its_stored_request_even_when_its_stored_hash_came_from_an_older_scheme()
    {
        var recording = new InvestigationCapture();
        recording.BeginRecording();
        await recording.ThroughAsync(CapturingObservationQueries.Kind, Query(("metricName", "redis_connections"), ("minimumSeverity", "warning")), () => Task.FromResult("captured answer"));
        CapturedResponse[] stored = [.. recording.EndRecording().Select(response => response with { RequestHash = "hash-from-an-older-scheme" })];

        var replay = new InvestigationCapture();
        replay.ServeFrom(stored);
        string served = await replay.ThroughAsync(
            CapturingObservationQueries.Kind,
            Query(("minimumSeverity", "warning"), ("metricName", "redis_connections")),
            () => Task.FromException<string>(new InvalidOperationException("A live provider was called during replay.")));

        served.ShouldBe("captured answer");
        replay.Misses.ShouldBe(0);
    }

    private static ObservationQuery Query(params (string Name, string Value)[] filters) => new()
    {
        TenantId = TenantId.From("acme"),
        EnvironmentId = EnvironmentId.From("reference"),
        ServiceId = ServiceId.From("checkout-service"),
        Kind = ObservationKind.Metric,
        Range = new TimeRange(Start, Start.AddMinutes(15)),
        Filters = filters.ToDictionary(filter => filter.Name, filter => filter.Value)
    };
}
