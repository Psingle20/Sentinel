using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

/// <summary>
/// Runtime asked Executor to run the approved plan. The version of this event in the stream is
/// the fencing token (ADR-004, ADR-006).
/// </summary>
public sealed record ExecutionRequested(PlanId PlanId, int PlanVersion, long FencingToken, string IdempotencyKey, DateTimeOffset OccurredAt) : IIncidentEvent;
