using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Domain.Sources;

namespace Sentinel.Ingestion.Application.Abstractions.Persistence;

public interface ISourceStateStore
{
    Task<SourceState?> GetAsync(string sourceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceState>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Saves inside the current unit of work, so a checkpoint moves only with its observations.</summary>
    Task SaveAsync(SourceState state, CancellationToken cancellationToken);
}
