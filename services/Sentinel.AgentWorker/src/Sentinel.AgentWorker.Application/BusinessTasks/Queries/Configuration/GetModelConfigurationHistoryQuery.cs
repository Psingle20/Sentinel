using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;

namespace Sentinel.AgentWorker.Application.BusinessTasks.Queries.Configuration;

public sealed record GetModelConfigurationHistoryQuery : IQuery<IReadOnlyList<DocumentVersion>>;
