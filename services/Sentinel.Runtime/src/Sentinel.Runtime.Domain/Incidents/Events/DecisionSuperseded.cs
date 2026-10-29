using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

/// <summary>A later observation superseded the current decision; it stays in the history (Phase 05).</summary>
public sealed record DecisionSuperseded(DecisionId DecisionId, DecisionId SupersededBy, string Reason, DateTimeOffset OccurredAt) : IIncidentEvent;
