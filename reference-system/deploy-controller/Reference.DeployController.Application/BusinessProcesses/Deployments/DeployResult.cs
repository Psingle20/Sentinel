using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Application.BusinessProcesses.Deployments;

/// <summary>How a deployment request ended, with the deployment it started, replayed or conflicted with.</summary>
public sealed record DeployResult(DeployOutcome Outcome, DeploymentRecord? Deployment);
