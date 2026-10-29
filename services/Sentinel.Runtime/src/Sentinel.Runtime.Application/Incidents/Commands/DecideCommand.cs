using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Incidents.Commands;

/// <summary>
/// Decides from the incident's own evidence, inside the writer's turn, so the decision is a pure
/// function of the stream and the versioned settings: consensus with the verifier's challenges
/// (Phase 05), or the walking skeleton's falsification check. Repeating it changes nothing.
/// </summary>
public sealed record DecideCommand(DecisionId DecisionId, bool UseConsensus, ConsensusSettings Settings) : IncidentCommand;
