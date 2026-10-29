using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Ingestion.Application.Abstractions.Providers;

public interface ITracesProvider
{
    string Name { get; }

    Task<ProviderResult<TraceObservation>> QueryRangeAsync(TraceRangeRequest request, CancellationToken cancellationToken);
}
