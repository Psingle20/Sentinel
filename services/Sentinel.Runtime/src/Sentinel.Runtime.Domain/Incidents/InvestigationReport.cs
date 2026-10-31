using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

public sealed record InvestigationReport
{
    public required InvestigationId InvestigationId { get; init; }

    public required AgentExecutionId AgentExecutionId { get; init; }

    public required InvestigationResult Result { get; init; }

    public IReadOnlyList<Evidence> Evidence { get; init; } = [];

    public IReadOnlyList<Hypothesis> Hypotheses { get; init; } = [];

    public string? AbstentionReason { get; init; }

    public required string ModelProvider { get; init; }

    public required string Model { get; init; }

    public string? ModelVersion { get; init; }

    public required string PromptVersion { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>The specialist role that produced the report; null is the generalist.</summary>
    public string? Role { get; init; }

    /// <summary>The verifier's challenges (Phase 05); empty for other roles.</summary>
    public IReadOnlyList<Challenge> Challenges { get; init; } = [];

    /// <summary>What the investigator could not determine, for the decision's missing-evidence record.</summary>
    public IReadOnlyList<string> Unknowns { get; init; } = [];

    /// <summary>Instructions to AI agents its untrusted tool results held: recorded to be shown, never acted on.</summary>
    public IReadOnlyList<UntrustedInstruction> SuspectedInjections { get; init; } = [];

    /// <summary>The tool catalog the investigator could call, for the version manifest (Phase 07).</summary>
    public string? ToolCatalogVersion { get; init; }

    /// <summary>The escalation investigator's discriminating query, as a sentence (Phase E05); null for other roles.</summary>
    public string? Query { get; init; }
}
