using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>One recovery signal as measured: its last value after the action, whether it met the criterion, and its average over an equal window before the action. Measurements recorded before Phase 08 have no baseline.</summary>
public sealed record Measurement(string ServiceId, string MetricName, string Comparison, double Threshold, double? ObservedValue, bool? Met, double? BaselineValue = null);
