using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessProcesses.Deployments;

/// <summary>One deployment; nothing when the ID is unknown.</summary>
public sealed record GetDeploymentRequest(string DeploymentId) : IRequest<DeploymentRecord?>;
