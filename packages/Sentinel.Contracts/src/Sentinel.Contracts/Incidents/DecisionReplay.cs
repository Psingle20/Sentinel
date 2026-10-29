using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>A recorded decision re-run on the stream prefix it was made from.</summary>
public sealed record DecisionReplay(
    DecisionId DecisionId,
    long StreamVersion,
    string? AlgorithmVersion,
    bool SettingsRecorded,
    DecisionOutcome RecordedOutcome,
    DecisionOutcome ReplayedOutcome,
    HypothesisId? RecordedLeader,
    HypothesisId? ReplayedLeader,
    double RecordedConfidence,
    double ReplayedConfidence,
    string? RecordedInputsHash,
    string? ReplayedInputsHash,
    bool Matches);
