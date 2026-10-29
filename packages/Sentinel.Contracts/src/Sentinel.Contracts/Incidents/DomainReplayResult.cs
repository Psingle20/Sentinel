using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>The result of a level-1 domain replay. Read-only: nothing is written, sent or executed.</summary>
public sealed record DomainReplayResult
{
    public ReplayLevel Level { get; init; } = ReplayLevel.Domain;

    /// <summary>Domain replay is deterministic by construction: it never calls a provider or a model.</summary>
    public bool Deterministic { get; init; } = true;

    public required IncidentId IncidentId { get; init; }

    public required long StreamVersion { get; init; }

    public required int EventCount { get; init; }

    /// <summary>SHA-256 of the canonical state rebuilt from the stream.</summary>
    public required string StateHash { get; init; }

    /// <summary>Whether a second rebuild of the same stream produced the same state hash.</summary>
    public required bool StateStable { get; init; }

    /// <summary>Whether the projection rebuilt in memory equals the stored one.</summary>
    public required bool ProjectionMatches { get; init; }

    public IReadOnlyList<string> ProjectionDifferences { get; init; } = [];

    public IReadOnlyList<DecisionReplay> Decisions { get; init; } = [];

    public required DateTimeOffset ReplayedAt { get; init; }
}
