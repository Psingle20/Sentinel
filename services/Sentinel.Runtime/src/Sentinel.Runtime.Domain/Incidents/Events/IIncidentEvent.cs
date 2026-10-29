using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

/// <summary>
/// An event in an incident's stream. The stream is the authoritative record of the incident
/// (ADR-004): its state is rebuilt by applying these events in version order.
/// </summary>
public interface IIncidentEvent : IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
