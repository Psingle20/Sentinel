using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

public sealed record IncidentEscalated(string Reason, DateTimeOffset OccurredAt) : IIncidentEvent;
