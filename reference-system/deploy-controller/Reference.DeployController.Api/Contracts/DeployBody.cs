using Reference.DeployController.Domain.Revisions;

namespace Reference.DeployController.Api.Contracts;

public sealed record DeployBody(
    string Version,
    DeploymentKind Kind = DeploymentKind.Deploy,
    string? Actor = null,
    string? Reason = null,
    int? RolloutSeconds = null,
    string? ConfigurationDiff = null);
