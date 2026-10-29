using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

public sealed record InvestigationReported(
    InvestigationReport Report,
    IReadOnlyList<RejectedEvidence> RejectedEvidence,
    DateTimeOffset OccurredAt) : IIncidentEvent;
