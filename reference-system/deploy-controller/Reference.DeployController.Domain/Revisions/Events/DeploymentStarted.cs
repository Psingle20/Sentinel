using CleanArchitecture.BuildingBlocks;

namespace Reference.DeployController.Domain.Revisions.Events;

public sealed record DeploymentStarted(DeploymentRecord Deployment) : IDomainEvent;
