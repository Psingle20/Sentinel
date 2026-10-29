namespace Reference.DeployController.Domain.Revisions;

/// <summary>
/// One deployment. The new revision serves only from <see cref="CompletedAt"/>: the reference
/// rollout switches all traffic at completion, so "started" and "serving" are different times.
/// </summary>
public sealed record DeploymentRecord
{
    public required string DeploymentId { get; init; }

    public required string Service { get; init; }

    public required string Version { get; init; }

    public string? PreviousVersion { get; init; }

    public required DeploymentKind Kind { get; init; }

    public required string Actor { get; init; }

    public string? Reason { get; init; }

    public string? ConfigurationDiff { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public required DeploymentStatus Status { get; init; }

    public string? IdempotencyKey { get; init; }

    public string? Error { get; init; }
}
