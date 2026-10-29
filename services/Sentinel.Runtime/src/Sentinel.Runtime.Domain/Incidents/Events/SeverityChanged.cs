using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

public sealed record SeverityChanged(IncidentSeverity Severity, string Reason, DateTimeOffset OccurredAt) : IIncidentEvent;
