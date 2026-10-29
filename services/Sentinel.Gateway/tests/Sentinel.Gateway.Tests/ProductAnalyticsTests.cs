using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Gateway.Application.Analytics;
using Shouldly;
using Sentinel.Gateway.Application.Abstractions.Persistence;
using Sentinel.Gateway.Application.BusinessTasks.Commands.Analytics;
using Sentinel.Gateway.Application.BusinessTasks.Queries.Analytics;

namespace Sentinel.Gateway.Tests;

/// <summary>Phase 08 product analytics: approved events only, kept as daily counts.</summary>
public sealed class ProductAnalyticsTests
{
    private sealed class InMemoryAnalytics : IAnalyticsStore
    {
        public Dictionary<(DateOnly Day, string Event, string Dimensions), long> Counts { get; } = [];

        public Task IncrementAsync(DateOnly day, string eventName, string dimensions, CancellationToken cancellationToken)
        {
            Counts[(day, eventName, dimensions)] = Counts.GetValueOrDefault((day, eventName, dimensions)) + 1;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AnalyticsCount>> SinceAsync(DateOnly from, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AnalyticsCount>>([.. Counts.Where(entry => entry.Key.Day >= from).Select(entry => new AnalyticsCount(entry.Key.Day, entry.Key.Event, entry.Key.Dimensions, entry.Value))]);
    }

    private readonly InMemoryAnalytics _store = new();

    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero));

    private RecordAnalyticsEventCommandHandler Recorder => new(_store, Clock);

    private GetRecentAnalyticsQueryHandler Reader => new(_store, Clock);

    [Fact]
    public async Task An_approved_event_is_counted_per_day_with_its_properties_in_a_canonical_order()
    {
        await Recorder.Handle(new RecordAnalyticsEventCommand(new AnalyticsEvent("incident.viewed", new Dictionary<string, string> { ["status"] = "awaitingApproval", ["severity"] = "critical" })), CancellationToken.None);
        await Recorder.Handle(new RecordAnalyticsEventCommand(new AnalyticsEvent("incident.viewed", new Dictionary<string, string> { ["severity"] = "critical", ["status"] = "awaitingApproval" })), CancellationToken.None);

        _store.Counts.ShouldBe(new Dictionary<(DateOnly, string, string), long> { [(new DateOnly(2026, 10, 29), "incident.viewed", "severity=critical;status=awaitingApproval")] = 2 });
    }

    [Fact]
    public async Task An_event_outside_the_catalog_is_refused()
    {
        Result recorded = await Recorder.Handle(new RecordAnalyticsEventCommand(new AnalyticsEvent("user.profile", null)), CancellationToken.None);

        recorded.Error.ShouldBe(AnalyticsCatalog.UnknownEvent);
        _store.Counts.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("incidentId", "0192f0c4-0000-7000-8000-000000000001")]
    [InlineData("user", "responder@example.test")]
    [InlineData("severity", "a free-text note")]
    public async Task Identifying_or_free_text_properties_are_refused(string property, string value)
    {
        Result recorded = await Recorder.Handle(new RecordAnalyticsEventCommand(new AnalyticsEvent("incident.viewed", new Dictionary<string, string> { [property] = value })), CancellationToken.None);

        recorded.Error.Code.ShouldBe("Analytics.UnknownProperty");
        _store.Counts.ShouldBeEmpty();
    }

    [Fact]
    public async Task Operators_read_the_recent_counts()
    {
        await Recorder.Handle(new RecordAnalyticsEventCommand(new AnalyticsEvent("admin.viewed", new Dictionary<string, string> { ["area"] = "policy" })), CancellationToken.None);

        IReadOnlyList<AnalyticsCount> counts = (await Reader.Handle(new GetRecentAnalyticsQuery(30), CancellationToken.None)).Value;

        counts.ShouldHaveSingleItem().Dimensions.ShouldBe("area=policy");
    }
}
