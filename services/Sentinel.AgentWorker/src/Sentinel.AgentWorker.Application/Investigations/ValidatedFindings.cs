using System.Globalization;
using System.Text.Json;
using Sentinel.Contracts;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>What the model produced, once validated: findings with Sentinel IDs, or the reasons it was refused.</summary>
public sealed record ValidatedFindings(
    IReadOnlyList<EvidenceFinding> Evidence,
    IReadOnlyList<HypothesisFinding> Hypotheses,
    bool Abstained,
    string? AbstentionReason,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string>? Unknowns = null)
{
    public bool IsValid => Errors.Count == 0;
}
