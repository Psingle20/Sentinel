using CleanArchitecture.BuildingBlocks;

namespace Reference.DeployController.Domain.Revisions.Events;

public sealed record ServiceReset(DeploymentRecord Deployment) : IDomainEvent;
