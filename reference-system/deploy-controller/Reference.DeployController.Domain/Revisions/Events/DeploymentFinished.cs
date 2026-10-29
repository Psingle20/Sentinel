using CleanArchitecture.BuildingBlocks;

namespace Reference.DeployController.Domain.Revisions.Events;

/// <summary>A rollout completed or failed; <see cref="DeploymentRecord.Status"/> says which.</summary>
public sealed record DeploymentFinished(DeploymentRecord Deployment) : IDomainEvent;
