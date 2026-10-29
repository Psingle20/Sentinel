using Sentinel.Contracts.Incidents;

namespace Sentinel.Api.Application.Incidents;

/// <summary>One page of a long list (Phase 08 API principles: large timelines and evidence sets are paginated).</summary>
public sealed record Page<T>(IReadOnlyList<T> Items, int Total, string? Next);

public static class Pages
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    /// <summary>Timeline entries newest first, before a stream version; <see cref="Page{T}.Next"/> is the cursor for older entries.</summary>
    public static Page<TimelineEntry> Timeline(IReadOnlyList<TimelineEntry> timeline, long? before, int? limit)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        int size = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        TimelineEntry[] older = [.. timeline.Where(entry => before is null || entry.StreamVersion < before).OrderByDescending(entry => entry.StreamVersion)];
        TimelineEntry[] page = [.. older.Take(size)];
        string? next = older.Length > size ? page[^1].StreamVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return new Page<TimelineEntry>(page, timeline.Count, next);
    }

    /// <summary>Evidence in the order it was accepted, from an offset; <see cref="Page{T}.Next"/> is the next offset.</summary>
    public static Page<EvidenceView> Evidence(IReadOnlyList<EvidenceView> evidence, int? offset, int? limit)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        int start = Math.Max(offset ?? 0, 0);
        int size = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        EvidenceView[] page = [.. evidence.Skip(start).Take(size)];
        string? next = start + page.Length < evidence.Count ? (start + page.Length).ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
        return new Page<EvidenceView>(page, evidence.Count, next);
    }
}
