using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Domain.Sources;

namespace Sentinel.Ingestion.Application.Abstractions.Persistence;

public sealed record ObservationPage(IReadOnlyList<Observation> Observations, bool Truncated, string? ContinuationToken);
