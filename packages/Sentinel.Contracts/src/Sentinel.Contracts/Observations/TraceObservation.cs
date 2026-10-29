using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Observations;

public sealed record TraceObservation : Observation
{
    public required string TraceId { get; init; }

    public required string SpanId { get; init; }

    public string? ParentSpanId { get; init; }

    public required string OperationName { get; init; }

    public required TimeSpan Duration { get; init; }

    public required SpanStatus Status { get; init; }

    public ServiceId? DependencyServiceId { get; init; }
}
