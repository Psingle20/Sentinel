using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

public sealed record IncidentResolved(string Reason, DateTimeOffset OccurredAt) : IIncidentEvent;
