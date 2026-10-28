using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reference.Scenarios;

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

public sealed record ScenarioStep
{
    public required int AtSeconds { get; init; }

    /// <summary><c>deploy</c>, <c>configChange</c>, <c>startBatch</c> or <c>stopBatch</c>.</summary>
    public required string Action { get; init; }

    public required string Service { get; init; }

    public string? Version { get; init; }

    public int? RolloutSeconds { get; init; }

    public int? TargetConnections { get; init; }

    public double? ConnectionsPerSecond { get; init; }

    public string? ConfigurationDiff { get; init; }

    /// <summary><c>cause</c>, <c>confounder</c> or <c>noise</c>.</summary>
    public required string Role { get; init; }
}

public sealed record ScenarioCause(string Description, string Service, string Change, int ActivationSeconds);

public sealed record ScenarioConfounder(string Description, string Service, string Change, int AtSeconds, int? ServingFromSeconds);

public sealed record ScenarioDetection(bool ExpectIncident, string Service, string Metric, double Threshold, int WithinSeconds);

/// <summary>A PromQL expression and what it must show between two offsets from the first step.</summary>
public sealed record SignalExpectation(string Description, string Query, string Expectation, double Threshold, int FromSeconds, int WithinSeconds);

public sealed record ScenarioNoise(double TrafficRequestsPerSecond, int Seed, double LatencyJitter, int TimingToleranceSeconds, string Notes);

public sealed record HypothesisExpectation(string Service, string Cause);

public sealed record ScenarioHypotheses(HypothesisExpectation Leading, IReadOnlyList<HypothesisExpectation> Rejected);

public sealed record RemediationExpectation(string Action, string Service, string? FromVersion = null, string? ToVersion = null, string? Mode = null);

public sealed record ScenarioRemediation(IReadOnlyList<RemediationExpectation> Permitted, IReadOnlyList<RemediationExpectation> FalseRemediations);

/// <summary><c>rollback</c>, <c>abstain</c> or <c>noIncident</c>, and whether that outcome is correct.</summary>
public sealed record BaselineExpectation(string Outcome, string? Service, string? FromVersion, string? ToVersion, bool Correct);

public sealed record RecoveryExpectation(string Description, string Query, string Comparison, double Threshold);

public sealed record ScenarioRecovery(string RestoredBy, IReadOnlyList<RecoveryExpectation> Criteria, int ObservationWindowSeconds, int EvaluationTimeoutSeconds);

/// <summary>Loads and validates every manifest in a directory.</summary>
public sealed class ScenarioCatalog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] Actions = ["deploy", "configChange", "startBatch", "stopBatch"];
    private static readonly string[] Kinds = ["golden", "confounder", "control"];
    private static readonly string[] BaselineOutcomes = ["rollback", "abstain", "noIncident"];
    private static readonly string[] Expectations = ["above", "below"];
    private static readonly string[] Comparisons = ["lessThan", "lessThanOrEqual", "greaterThan", "greaterThanOrEqual"];

    private ScenarioCatalog(IReadOnlyDictionary<string, ScenarioManifest> manifests) => Manifests = manifests;

    public IReadOnlyDictionary<string, ScenarioManifest> Manifests { get; }

    public static ScenarioCatalog Load(string directory)
    {
        var manifests = new Dictionary<string, ScenarioManifest>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            ScenarioManifest manifest = JsonSerializer.Deserialize<ScenarioManifest>(File.ReadAllText(path), Json)
                ?? throw new InvalidDataException($"{path} is empty.");
            errors.AddRange(Validate(manifest).Select(error => $"{Path.GetFileName(path)}: {error}"));
            if (!manifests.TryAdd(manifest.Id, manifest))
            {
                errors.Add($"{Path.GetFileName(path)}: duplicate scenario ID {manifest.Id}.");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException("Invalid scenario manifests:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        return new ScenarioCatalog(manifests);
    }

    /// <summary>Checks the fields every manifest must define (Phase 01 scenario contract).</summary>
    public static IReadOnlyList<string> Validate(ScenarioManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var errors = new List<string>();
        Require(Kinds.Contains(manifest.Kind), $"kind '{manifest.Kind}' is not one of {string.Join(", ", Kinds)}.");
        Require(manifest.Version >= 1, "version must be at least 1.");
        Require(manifest.WarmupSeconds >= 0 && manifest.DurationSeconds > 0, "warmup and duration must be positive.");
        Require(manifest.Steps.Count > 0, "a scenario needs at least one step.");
        Require(manifest.Steps.Select(step => step.AtSeconds).SequenceEqual(manifest.Steps.Select(step => step.AtSeconds).Order()), "steps must be in time order.");

        foreach (ScenarioStep step in manifest.Steps)
        {
            Require(Actions.Contains(step.Action), $"step action '{step.Action}' is unknown.");
            Require(step.Action != "deploy" || step.Version is not null, $"deploy step at {step.AtSeconds}s needs a version.");
            Require(step.AtSeconds <= manifest.DurationSeconds, $"step at {step.AtSeconds}s is after the scenario ends.");
        }

        foreach (SignalExpectation signal in manifest.ExpectedObservations.Concat(manifest.DiscriminatingSignals))
        {
            Require(Expectations.Contains(signal.Expectation), $"expectation '{signal.Expectation}' must be above or below.");
            Require(signal.FromSeconds <= signal.WithinSeconds, $"'{signal.Description}' has its window reversed.");
        }

        Require(BaselineOutcomes.Contains(manifest.Baseline.Outcome), $"baseline outcome '{manifest.Baseline.Outcome}' is unknown.");
        Require(manifest.Baseline.Outcome != "rollback" || manifest.Baseline.Service is not null, "a rollback baseline names its service.");

        if (manifest.Detection.ExpectIncident)
        {
            Require(manifest.Cause is not null, "a scenario that opens an incident names its injected cause.");
            Require(manifest.Hypotheses is not null, "a scenario that opens an incident names its expected hypotheses.");
            Require(manifest.Recovery is { Criteria.Count: > 0 }, "a scenario that opens an incident defines recovery criteria.");
            Require(manifest.DiscriminatingSignals.Count > 0, "a scenario that opens an incident names its discriminating signals.");
            Require(manifest.ExpectedObservations.Count > 0, "a scenario that opens an incident lists expected observations.");
        }
        else
        {
            Require(manifest.Baseline.Outcome == "noIncident", "a control scenario expects no incident from the baseline.");
        }

        foreach (RecoveryExpectation criterion in manifest.Recovery?.Criteria ?? [])
        {
            Require(Comparisons.Contains(criterion.Comparison), $"recovery comparison '{criterion.Comparison}' is unknown.");
        }

        return errors;

        void Require(bool condition, string error)
        {
            if (!condition)
            {
                errors.Add(error);
            }
        }
    }
}
