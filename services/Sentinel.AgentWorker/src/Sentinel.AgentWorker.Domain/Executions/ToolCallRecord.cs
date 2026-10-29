using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Domain.Executions;

/// <summary>
/// One tool call and the canonical result it returned, kept for audit and for investigation
/// replay (ADR-007, Phase 07 level 2).
/// </summary>
public sealed record ToolCallRecord
{
    public required string ToolCallId { get; init; }

    public required string ToolName { get; init; }

    public required string Arguments { get; init; }

    /// <summary>The exact content returned to the model.</summary>
    public required string Result { get; init; }

    public IReadOnlyList<ObservationId> ObservationIds { get; init; } = [];

    /// <summary>True when the data behind the result was incomplete, truncated or stale.</summary>
    public bool Partial { get; init; }

    public string? Error { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required TimeSpan Duration { get; init; }
}
