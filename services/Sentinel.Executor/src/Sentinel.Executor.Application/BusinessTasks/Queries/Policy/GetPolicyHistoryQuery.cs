using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.Abstractions.Persistence;

namespace Sentinel.Executor.Application.BusinessTasks.Queries.Policy;

public sealed record GetPolicyHistoryQuery : IQuery<IReadOnlyList<PolicyVersionInfo>>;
