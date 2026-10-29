using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

/// <summary>An investigation did not report within its budget; the incident proceeds without it.</summary>
public sealed record InvestigationTimedOut(InvestigationId InvestigationId, DateTimeOffset OccurredAt) : IIncidentEvent;
