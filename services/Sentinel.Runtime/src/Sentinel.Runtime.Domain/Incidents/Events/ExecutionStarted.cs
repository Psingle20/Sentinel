using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents.Events;

/// <summary>The point of no return: Runtime confirmed Executor may cause side effects.</summary>
public sealed record ExecutionStarted(PlanId PlanId, int PlanVersion, long FencingToken, DateTimeOffset OccurredAt) : IIncidentEvent;
