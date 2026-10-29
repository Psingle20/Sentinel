using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

public sealed record ExecutionView(PlanId PlanId, int PlanVersion, ExecutionOutcome? Outcome, string? Detail, DateTimeOffset RequestedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);
