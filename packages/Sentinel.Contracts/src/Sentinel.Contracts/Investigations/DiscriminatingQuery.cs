using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Investigations;

/// <summary>
/// The one observation the escalation investigator proposes to separate two explanations consensus could not (Phase E05):
/// an existing read-only investigation tool with its arguments, and what each explanation predicts it will show.
/// AgentWorker runs the tool and compares the result with the predictions; the model never judges its own experiment.
/// </summary>
public sealed record DiscriminatingQuery
{
    /// <summary>The two hypotheses the observation separates.</summary>
    public required IReadOnlyList<HypothesisId> HypothesisIds { get; init; }

    /// <summary>An investigation tool from the catalogue, for example <c>metric_range</c>.</summary>
    public required string Tool { get; init; }

    /// <summary>The tool's arguments as JSON, inside the incident's observation window.</summary>
    public required string Arguments { get; init; }

    /// <summary>What each hypothesis predicts the observation will show, one per hypothesis.</summary>
    public required IReadOnlyList<QueryExpectation> Expectations { get; init; }

    /// <summary>Why this one observation suffices to separate them.</summary>
    public required string Rationale { get; init; }
}
