using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessProcesses.Deployments;

/// <summary>A deployment, rollback or configuration change of one managed service, idempotent on its key.</summary>
public sealed record StartDeploymentRequest(string Service, string Version, DeploymentKind Kind, string? Actor, string? Reason, int? RolloutSeconds, string? ConfigurationDiff, string? IdempotencyKey) : IRequest<DeployResult>;
