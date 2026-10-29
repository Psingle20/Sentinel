using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>A recovery signal after the action, and its pre-action baseline (Phase 08 before-and-after view; absent before it).</summary>
public sealed record RecoveryMeasurement(RecoveryCriterion Criterion, double? ObservedValue, bool? Met, double? BaselineValue = null);
