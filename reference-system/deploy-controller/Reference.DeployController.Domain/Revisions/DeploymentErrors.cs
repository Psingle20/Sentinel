using CleanArchitecture.BuildingBlocks;

namespace Reference.DeployController.Domain.Revisions;

public static class DeploymentErrors
{
    public static readonly Error Conflict = Error.Conflict("Deployment.Conflict", "Another rollout of the service is in progress.");

    public static Error UnknownRevision(string version, string service) =>
        Error.Problem("Deployment.UnknownRevision", $"{version} is not a revision of {service}.");
}
