using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Ingestion.Application.Abstractions.Providers;

public interface ILogsProvider
{
    string Name { get; }

    Task<ProviderResult<LogObservation>> QueryRangeAsync(LogRangeRequest request, CancellationToken cancellationToken);
}
