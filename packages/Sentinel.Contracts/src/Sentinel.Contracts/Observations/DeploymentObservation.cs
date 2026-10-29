using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Observations;

/// <summary>
/// A deployment or configuration change. The deploy system reports one observation when the
/// change starts and another when it completes or fails, so <see cref="CompletedAt"/> is null
/// while it is in progress.
/// </summary>
public sealed record DeploymentObservation : Observation
{
    public required string DeploymentId { get; init; }

    public required string Version { get; init; }

    /// <summary>The version this deployment replaced, when the deploy system reports it.</summary>
    public string? PreviousVersion { get; init; }

    public string? Revision { get; init; }

    public string? CommitSha { get; init; }

    public string? Actor { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? ConfigurationDiff { get; init; }

    /// <summary><c>deploy</c>, <c>rollback</c> or <c>config-change</c>.</summary>
    public string ChangeKind { get; init; } = "deploy";

    /// <summary><c>in-progress</c>, <c>completed</c> or <c>failed</c>.</summary>
    public string Status { get; init; } = "completed";
}
