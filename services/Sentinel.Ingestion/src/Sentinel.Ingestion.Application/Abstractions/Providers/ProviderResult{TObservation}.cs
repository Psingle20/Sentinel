using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;

namespace Sentinel.Ingestion.Application.Abstractions.Providers;

/// <summary>
/// What a provider returned (ADR-014): canonical observations, the event time up to which they
/// are complete, whether limits truncated them and the provider's health. An outage is a
/// result with <see cref="ProviderStatus.Unavailable"/>, never an exception or an empty success.
/// </summary>
public sealed record ProviderResult<TObservation>(
    IReadOnlyList<TObservation> Observations,
    DateTimeOffset Watermark,
    bool Truncated,
    ProviderStatus Status,
    string? Error = null)
    where TObservation : Observation
{
    public static ProviderResult<TObservation> Unavailable(DateTimeOffset previousWatermark, string error) =>
        new([], previousWatermark, false, ProviderStatus.Unavailable, error);
}
