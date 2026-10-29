using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

/// <summary>A change learned after detection, such as a deployment completing, that bears on the incident.</summary>
public sealed record ChangeUpdated(LinkedChange Change, DateTimeOffset OccurredAt) : IIncidentEvent;
