using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;

namespace Sentinel.Runtime.Application.BusinessTasks.Queries.Configuration;

public sealed record GetRuntimeConfigurationHistoryQuery : IQuery<IReadOnlyList<DocumentVersion>>;
