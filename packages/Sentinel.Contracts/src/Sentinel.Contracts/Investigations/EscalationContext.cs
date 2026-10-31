namespace Sentinel.Contracts.Investigations;

/// <summary>
/// The structured state the escalation investigator reasons over (Phase E05): how each surviving explanation stood when
/// consensus abstained for ambiguity, and how complete the evidence was. Never raw telemetry.
/// </summary>
public sealed record EscalationContext
{
    public required IReadOnlyList<HypothesisStanding> Standing { get; init; }

    public required double EvidenceCompleteness { get; init; }

    /// <summary>Why consensus abstained, in its own words.</summary>
    public required string Rationale { get; init; }
}
