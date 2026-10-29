using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;

namespace Sentinel.Ingestion.Application.BusinessTasks.Queries.Configuration;

public sealed record GetSourceConfigurationHistoryQuery : IQuery<IReadOnlyList<DocumentVersion>>;
