using CleanArchitecture.BuildingBlocks.Messaging;

namespace Reference.DeployController.Application.BusinessProcesses.Deployments;

/// <summary>A rollout has run its course: switch the service to the new revision, or record why it failed.</summary>
public sealed record CompleteRolloutRequest(string DeploymentId) : IRequest;
