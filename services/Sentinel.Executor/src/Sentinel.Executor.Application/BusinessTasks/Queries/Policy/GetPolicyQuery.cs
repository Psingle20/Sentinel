using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.BusinessTasks.Queries.Policy;

public sealed record GetPolicyQuery : IQuery<PolicyDocument>;
