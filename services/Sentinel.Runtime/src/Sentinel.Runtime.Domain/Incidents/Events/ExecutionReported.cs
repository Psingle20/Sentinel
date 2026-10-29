using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

public sealed record ExecutionReported(PlanId PlanId, int PlanVersion, ExecutionResult Result, string? Detail, DateTimeOffset OccurredAt) : IIncidentEvent;
