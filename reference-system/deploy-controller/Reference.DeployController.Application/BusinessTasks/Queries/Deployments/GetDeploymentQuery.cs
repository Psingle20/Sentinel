using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessTasks.Queries.Deployments;

public sealed record GetDeploymentQuery(string DeploymentId) : IQuery<DeploymentRecord?>;
