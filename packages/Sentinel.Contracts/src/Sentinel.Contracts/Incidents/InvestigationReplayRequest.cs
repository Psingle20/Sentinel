using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>Asks AgentWorker to replay one agent execution at level 2 or 3.</summary>
public sealed record InvestigationReplayRequest
{
    public ReplayLevel Level { get; init; } = ReplayLevel.Investigation;

    /// <summary>Level 3 only: the model profile to reevaluate with; null keeps the original role's route.</summary>
    public string? ModelProfile { get; init; }

    /// <summary>Level 3 only: the prompt version to reevaluate with; null keeps the original.</summary>
    public string? PromptVersion { get; init; }
}
