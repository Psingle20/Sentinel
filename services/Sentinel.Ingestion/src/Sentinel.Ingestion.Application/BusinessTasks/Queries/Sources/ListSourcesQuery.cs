using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Ingestion.Application.BusinessProcesses.Sources;

namespace Sentinel.Ingestion.Application.BusinessTasks.Queries.Sources;

public sealed record ListSourcesQuery : IQuery<IReadOnlyList<SourceSummary>>;
