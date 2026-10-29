using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Incidents.Commands;

public sealed record InvestigationSpec(
    InvestigationId InvestigationId,
    string? Role,
    string Objective,
    DateTimeOffset WindowFrom,
    DateTimeOffset WindowTo,
    IReadOnlyList<HypothesisId>? ChallengedHypothesisIds = null,
    IReadOnlyList<PriorIncident>? SimilarIncidents = null);
