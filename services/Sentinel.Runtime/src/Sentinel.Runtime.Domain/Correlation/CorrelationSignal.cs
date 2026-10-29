using System.Globalization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Domain.Correlation;

/// <summary>A firing detection signal.</summary>
public sealed record CorrelationSignal(ServiceId ServiceId, string MetricName, double Value, DateTimeOffset Onset, DateTimeOffset EventTime, ObservationId ObservationId);
