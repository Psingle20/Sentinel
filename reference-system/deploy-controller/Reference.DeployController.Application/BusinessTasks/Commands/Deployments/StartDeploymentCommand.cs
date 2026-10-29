using CleanArchitecture.BuildingBlocks.Messaging;
using Reference.DeployController.Application.BusinessProcesses.Deployments;
using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessTasks.Commands.Deployments;

public sealed record StartDeploymentCommand(string Service, string Version, DeploymentKind Kind, string? Actor, string? Reason, int? RolloutSeconds, string? ConfigurationDiff, string? IdempotencyKey) : ICommand<DeployResult>;
