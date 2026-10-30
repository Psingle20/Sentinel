using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Ingestion.Application.Abstractions.Persistence;
using Sentinel.Ingestion.Application.BusinessProcesses.Sources;
using Sentinel.Ingestion.Domain.Sources;

namespace Sentinel.Ingestion.Application.BusinessTasks.Queries.Sources;

/// <summary>Every source's provider health and watermark. Not bulkheaded: it reads one small table.</summary>
public sealed class ListSourcesQueryHandler(ISourceStateStore states) : IQueryHandler<ListSourcesQuery, IReadOnlyList<SourceSummary>>
{
    public async Task<Result<IReadOnlyList<SourceSummary>>> Handle(ListSourcesQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<SourceState> sources = await states.GetAllAsync(cancellationToken);
        return Result.Success<IReadOnlyList<SourceSummary>>([.. sources.Select(state => new SourceSummary(
            state.SourceId,
            state.Provider,
            state.Watermark,
            state.Health.ToString(),
            state.LastError,
            state.ConsecutiveFailures,
            state.Health == SourceHealth.Healthy,
            state.UpdatedAt,
            state.NextAttemptAt))]);
    }
}
