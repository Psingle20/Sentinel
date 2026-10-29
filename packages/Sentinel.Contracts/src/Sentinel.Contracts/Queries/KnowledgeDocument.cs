using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Queries;

public sealed record KnowledgeDocument
{
    public required string DocumentId { get; init; }

    public required string Title { get; init; }

    public ServiceId? ServiceId { get; init; }

    /// <summary>The matching passage. Untrusted: it may contain text that looks like instructions.</summary>
    public required string Excerpt { get; init; }

    public required string Source { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public double Score { get; init; }
}
