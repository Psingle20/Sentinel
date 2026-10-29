using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// The result of replaying an agent execution. Inputs were served only from what the execution
/// captured; a call outside them failed explicitly rather than reaching a live provider.
/// </summary>
public sealed record InvestigationReplayResult
{
    public required Guid ReplayId { get; init; }

    public required AgentExecutionId AgentExecutionId { get; init; }

    public required ReplayLevel Level { get; init; }

    /// <summary>True only when every step was deterministic: captured inputs and a deterministic model, or no model call at all.</summary>
    public required bool Deterministic { get; init; }

    /// <summary>Whether re-running every recorded tool call over the captured provider responses reproduced its recorded result.</summary>
    public required bool InputsReproduced { get; init; }

    public required int ToolCallsReplayed { get; init; }

    public IReadOnlyList<ToolCallMismatch> ToolCallMismatches { get; init; } = [];

    /// <summary>Provider queries the replay needed that the execution never captured.</summary>
    public int UncapturedQueries { get; init; }

    /// <summary>Null when the investigator was not rerun (level 2 with a nondeterministic model).</summary>
    public bool? FindingsMatch { get; init; }

    public IReadOnlyList<string> Differences { get; init; } = [];

    /// <summary>Cited observations the replayed findings did not receive from the frozen inputs: fabricated evidence.</summary>
    public IReadOnlyList<ObservationId> UnsupportedCitations { get; init; } = [];

    public ModelMetadata? Model { get; init; }

    public InvestigationCompleted? Replayed { get; init; }

    public required string Explanation { get; init; }

    public required DateTimeOffset ReplayedAt { get; init; }
}
