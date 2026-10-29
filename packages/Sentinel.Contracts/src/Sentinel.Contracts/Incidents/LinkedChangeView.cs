using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

/// <summary>A change correlation linked to the incident, with its score and the reasons (Phase 03).</summary>
public sealed record LinkedChangeView(
    string DeploymentId,
    ServiceId ServiceId,
    string ChangeKind,
    string Version,
    string? PreviousVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset? ServingFrom,
    bool Failed,
    double? Score,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<ObservationId> ObservationIds);
