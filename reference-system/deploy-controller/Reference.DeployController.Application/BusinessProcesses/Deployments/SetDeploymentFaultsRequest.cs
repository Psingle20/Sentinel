using CleanArchitecture.BuildingBlocks.Messaging;

namespace Reference.DeployController.Application.BusinessProcesses.Deployments;

/// <summary>Injects or clears the rollback fault (Phase 07 failure scenarios).</summary>
public sealed record SetDeploymentFaultsRequest(bool FailRollbacks) : IRequest;
