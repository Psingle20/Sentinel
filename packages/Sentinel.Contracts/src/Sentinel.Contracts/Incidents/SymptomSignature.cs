using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>A symptom the incident was opened or merged for: which service, which signal.</summary>
public sealed record SymptomSignature(ServiceId ServiceId, string MetricName);
