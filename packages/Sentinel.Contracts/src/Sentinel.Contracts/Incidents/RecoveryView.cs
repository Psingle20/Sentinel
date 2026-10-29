using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

public sealed record RecoveryView(PlanId PlanId, RecoveryOutcome Outcome, IReadOnlyList<RecoveryMeasurement> Measurements, DateTimeOffset ObservedThrough, DateTimeOffset EvaluatedAt, string? Reason);
