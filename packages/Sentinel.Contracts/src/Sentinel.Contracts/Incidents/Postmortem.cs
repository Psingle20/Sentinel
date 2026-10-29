using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// A postmortem generated from authoritative structured history (Phase 07): every fact links to the
/// events and evidence it came from, and the narrative is the only part a human writes.
/// </summary>
public sealed record Postmortem
{
    public required IncidentId IncidentId { get; init; }

    public required string Title { get; init; }

    public required IncidentStatus Status { get; init; }

    public required long StreamVersion { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required PostmortemFact Summary { get; init; }

    public IReadOnlyList<PostmortemFact> Timeline { get; init; } = [];

    public IReadOnlyList<PostmortemFact> Evidence { get; init; } = [];

    public IReadOnlyList<PostmortemFact> Decision { get; init; } = [];

    public IReadOnlyList<PostmortemFact> Actions { get; init; } = [];

    public IReadOnlyList<PostmortemFact> Recovery { get; init; } = [];

    /// <summary>What the investigation could not determine, and dissent that was kept.</summary>
    public IReadOnlyList<PostmortemFact> OpenQuestions { get; init; } = [];

    public required VersionManifest Versions { get; init; }

    public PostmortemNarrative? Narrative { get; init; }
}
