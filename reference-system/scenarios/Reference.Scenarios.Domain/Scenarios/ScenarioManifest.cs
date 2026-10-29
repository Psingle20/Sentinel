using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reference.Scenarios.Domain.Scenarios;

/// <summary>
/// A scenario's ground truth and timeline (Phase 01 scenario contract). Sentinel never reads
/// manifests; only the scenario runner and the evaluation suites do.
/// </summary>
public sealed record ScenarioManifest
{
    public required string Id { get; init; }

    public required string Slug { get; init; }

    public required int Version { get; init; }

    /// <summary><c>golden</c>, <c>confounder</c> or <c>control</c>.</summary>
    public required string Kind { get; init; }

    public required string Description { get; init; }

    public required string AffectedService { get; init; }

    public IReadOnlyList<string> DownstreamServices { get; init; } = [];

    public required int WarmupSeconds { get; init; }

    public required int DurationSeconds { get; init; }

    public required IReadOnlyList<ScenarioStep> Steps { get; init; }

    public ScenarioCause? Cause { get; init; }

    public IReadOnlyList<ScenarioConfounder> Confounders { get; init; } = [];

    public required ScenarioDetection Detection { get; init; }

    public IReadOnlyList<SignalExpectation> ExpectedObservations { get; init; } = [];

    public IReadOnlyList<SignalExpectation> DiscriminatingSignals { get; init; } = [];

    public required ScenarioNoise Noise { get; init; }

    public ScenarioHypotheses? Hypotheses { get; init; }

    public required ScenarioRemediation Remediation { get; init; }

    public required BaselineExpectation Baseline { get; init; }

    public ScenarioRecovery? Recovery { get; init; }
}
