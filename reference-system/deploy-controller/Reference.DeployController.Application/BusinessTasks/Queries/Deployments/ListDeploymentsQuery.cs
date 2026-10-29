using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessTasks.Queries.Deployments;

public sealed record ListDeploymentsQuery(DateTimeOffset? Since, DateTimeOffset? Until, string? Service) : IQuery<IReadOnlyList<DeploymentRecord>>;
