namespace Reference.Scenarios.Domain.Scenarios;

/// <summary>The scenarios a runner can run, each satisfying the manifest rules, by ID.</summary>
public sealed class ScenarioCatalog
{
    private static readonly string[] Actions = ["deploy", "configChange", "startBatch", "stopBatch"];
    private static readonly string[] Kinds = ["golden", "confounder", "control"];
    private static readonly string[] BaselineOutcomes = ["rollback", "abstain", "noIncident"];
    private static readonly string[] Expectations = ["above", "below"];
    private static readonly string[] Comparisons = ["lessThan", "lessThanOrEqual", "greaterThan", "greaterThanOrEqual"];

    private ScenarioCatalog(IReadOnlyDictionary<string, ScenarioManifest> manifests) => Manifests = manifests;

    public IReadOnlyDictionary<string, ScenarioManifest> Manifests { get; }

    /// <summary>
    /// The catalog of <paramref name="manifests"/>, each named by its source; any manifest that breaks
    /// a rule, or repeats an ID, refuses the whole catalog with every reason.
    /// </summary>
    public static ScenarioCatalog From(IEnumerable<(string Source, ScenarioManifest Manifest)> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);

        var catalog = new Dictionary<string, ScenarioManifest>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach ((string source, ScenarioManifest manifest) in manifests)
        {
            errors.AddRange(Validate(manifest).Select(error => $"{source}: {error}"));
            if (!catalog.TryAdd(manifest.Id, manifest))
            {
                errors.Add($"{source}: duplicate scenario ID {manifest.Id}.");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException("Invalid scenario manifests:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        return new ScenarioCatalog(catalog);
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
