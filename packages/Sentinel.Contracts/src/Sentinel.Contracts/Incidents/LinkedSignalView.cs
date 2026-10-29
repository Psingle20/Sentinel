using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

/// <summary>A related service's symptom correlation merged into the incident.</summary>
public sealed record LinkedSignalView(ServiceId ServiceId, string MetricName, double Value, double Score, IReadOnlyList<string> Reasons, DateTimeOffset LinkedAt);
