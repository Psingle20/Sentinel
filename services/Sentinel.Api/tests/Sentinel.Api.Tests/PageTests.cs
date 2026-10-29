using Sentinel.Api.Application.Incidents;
using Sentinel.Contracts.Incidents;
using Shouldly;

namespace Sentinel.Api.Tests;

/// <summary>Phase 08 API principles: large timelines and evidence sets are paginated.</summary>
public sealed class PageTests
{
    private static readonly TimelineEntry[] Timeline = [.. Enumerable.Range(1, 120).Select(version => new TimelineEntry(version, DateTimeOffset.UnixEpoch.AddSeconds(version), "note", $"entry {version}"))];

    [Fact]
    public void The_timeline_is_read_newest_first_with_a_cursor_for_older_entries()
    {
        Page<TimelineEntry> first = Pages.Timeline(Timeline, before: null, limit: 50);
        Page<TimelineEntry> second = Pages.Timeline(Timeline, before: long.Parse(first.Next!, System.Globalization.CultureInfo.InvariantCulture), limit: 50);
        Page<TimelineEntry> last = Pages.Timeline(Timeline, before: long.Parse(second.Next!, System.Globalization.CultureInfo.InvariantCulture), limit: 50);

        first.Items.Select(entry => entry.StreamVersion).ShouldBe(Enumerable.Range(71, 50).Select(version => (long)version).Reverse());
        second.Items[0].StreamVersion.ShouldBe(70);
        last.Items.Count.ShouldBe(20);
        last.Next.ShouldBeNull();
        first.Total.ShouldBe(120);
    }

    [Fact]
    public void A_page_never_exceeds_the_maximum_size()
    {
        Pages.Timeline(Timeline, before: null, limit: 10_000).Items.Count.ShouldBe(Pages.MaxLimit > 120 ? 120 : Pages.MaxLimit);
        Pages.Timeline(Timeline, before: null, limit: 0).Items.Count.ShouldBe(1);
    }
}
