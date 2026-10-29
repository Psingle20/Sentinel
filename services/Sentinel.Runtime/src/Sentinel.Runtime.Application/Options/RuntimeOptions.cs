using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application;

/// <summary>Configured as <c>Runtime</c>.</summary>
public sealed class RuntimeOptions
{
    public const string Section = "Runtime";

    public IList<DetectionRuleOptions> Detection { get; } = [];

    /// <summary>The service catalog's dependency edges: a service and the services it calls.</summary>
    public IDictionary<string, string[]> Dependencies { get; } = new Dictionary<string, string[]>(StringComparer.Ordinal);

    /// <summary>The recovery signals a plan for each affected service must restore.</summary>
    public IDictionary<string, RecoveryCriterionOptions[]> Recovery { get; } = new Dictionary<string, RecoveryCriterionOptions[]>(StringComparer.Ordinal);

    /// <summary>Services that run background batches: a saturation they cause is remediated by pausing the batch (Phase 06).</summary>
    public IList<string> BatchServices { get; } = [];

    /// <summary>The service catalog's owning team per service.</summary>
    public IDictionary<string, string> Owners { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public CorrelationOptions Correlation { get; } = new();

    public InvestigationPlanOptions Investigation { get; } = new();

    public WorkflowOptions Workflow { get; } = new();

    public ConsensusOptions Consensus { get; } = new();

    public Memory.MemoryOptions Memory { get; } = new();

    public TimeSpan InvestigationWindow { get; set; } = TimeSpan.FromMinutes(15);

    public int InvestigationMaxToolCalls { get; set; } = 12;

    public int InvestigationMaxModelTokens { get; set; } = 40_000;

    public TimeSpan InvestigationTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How long after a verified action recovery is observed before it is judged.</summary>
    public TimeSpan ObservationWindow { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Extra wait after the window so that ingestion has caught up.</summary>
    public TimeSpan EvaluationLag { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>After this long past the window, missing or incomplete telemetry makes recovery inconclusive.</summary>
    public TimeSpan EvaluationTimeout { get; set; } = TimeSpan.FromMinutes(3);

    public InvestigationBudget InvestigationBudget() => new(InvestigationMaxToolCalls, InvestigationMaxModelTokens, InvestigationTimeout);

    public IReadOnlyList<DetectionRule> DetectionRules() =>
        [.. Detection.Select(rule => new DetectionRule
        {
            ServiceId = ServiceId.From(rule.Service),
            MetricName = rule.Metric,
            DetectionThreshold = rule.DetectionThreshold,
            OnsetThreshold = rule.OnsetThreshold,
            RequiredSamples = rule.RequiredSamples,
            Lookback = rule.Lookback,
            ClearAfter = rule.ClearAfter
        })];

    public IReadOnlyList<ServiceId> DependenciesOf(ServiceId service) =>
        Dependencies.TryGetValue(service.Value, out string[]? dependencies) ? [.. dependencies.Select(ServiceId.From)] : [];

    public IReadOnlyList<RecoveryCriterion> RecoveryCriteriaFor(ServiceId service) =>
        Recovery.TryGetValue(service.Value, out RecoveryCriterionOptions[]? criteria)
            ? [.. criteria.Select(criterion => new RecoveryCriterion
            {
                ServiceId = ServiceId.From(criterion.Service ?? service.Value),
                MetricName = criterion.Metric,
                Comparison = criterion.Comparison,
                Threshold = criterion.Threshold
            })]
            : [];

    public CorrelationSettings CorrelationSettings() => new()
    {
        MergeWindow = Correlation.MergeWindow,
        ReopenWindow = Correlation.ReopenWindow,
        MaxMergeDistance = Correlation.MaxMergeDistance,
        MinimumChangeScore = Correlation.MinimumChangeScore,
        ProximityScale = Correlation.ProximityScale
    };

    public IReadOnlyList<Suppression> Suppressions() =>
        [.. Correlation.Suppressions.Select(rule => new Suppression(rule.Service is { Length: > 0 } service ? ServiceId.From(service) : null, rule.Metric, rule.From, rule.Until, rule.Reason))];

    public ConsensusSettings ConsensusSettings() => new()
    {
        SourceReliability = new Dictionary<string, double>(Consensus.SourceReliability, StringComparer.Ordinal),
        DefaultReliability = Consensus.DefaultReliability,
        PartialWeight = Consensus.PartialWeight,
        ContradictionWeight = Consensus.ContradictionWeight,
        AbstainBelowConfidence = Consensus.AbstainBelowConfidence,
        MinimumIndependentSupport = Consensus.MinimumIndependentSupport,
        DissentMargin = Consensus.DissentMargin
    };

    public bool IsValid() =>
        Detection.Count > 0
        && Detection.All(rule => rule.DetectionThreshold > rule.OnsetThreshold && rule.RequiredSamples > 0)
        && ObservationWindow > TimeSpan.Zero
        && EvaluationTimeout > EvaluationLag
        && Investigation.Roles.All(InvestigationRoles.IsKnown)
        && Workflow.ApprovalTimeout > TimeSpan.Zero;
}

/// <summary>Correlation rules (Phase 03), configured as <c>Runtime:Correlation</c>.</summary>
public sealed class CorrelationOptions
{
    public TimeSpan MergeWindow { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan ReopenWindow { get; set; } = TimeSpan.FromMinutes(10);

    public int MaxMergeDistance { get; set; } = 1;

    public double MinimumChangeScore { get; set; } = 0.2;

    public TimeSpan ProximityScale { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long an observed dependency edge counts toward topology.</summary>
    public TimeSpan EdgeRetention { get; set; } = TimeSpan.FromHours(1);

    public IList<SuppressionOptions> Suppressions { get; } = [];
}

public sealed class SuppressionOptions
{
    public string? Service { get; set; }

    public string? Metric { get; set; }

    public DateTimeOffset From { get; set; }

    public DateTimeOffset Until { get; set; }

    public string Reason { get; set; } = "maintenance window";
}

/// <summary>Which investigators Runtime assigns (Phase 04) and whether hypotheses are challenged (Phase 05).</summary>
public sealed class InvestigationPlanOptions
{
    /// <summary>Roles investigated in parallel for every incident; <c>generalist</c> is the walking skeleton's single investigator.</summary>
    public IList<string> Roles { get; } = [];

    /// <summary>The configured roles, or the generalist alone when none are configured.</summary>
    public IReadOnlyList<string> EffectiveRoles => Roles.Count == 0 ? [InvestigationRoles.Generalist] : [.. Roles.Distinct(StringComparer.Ordinal)];

    /// <summary>Runs the adversarial verifier on the hypotheses before deciding.</summary>
    public bool Challenge { get; set; }

    /// <summary>How long the workflow waits beyond an investigation's own budget before giving up on it.</summary>
    public TimeSpan ReportGrace { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Waits and timers of the incident workflow (ADR-005).</summary>
public sealed class WorkflowOptions
{
    /// <summary>After this long without an approval or rejection the plan expires and the incident is escalated.</summary>
    public TimeSpan ApprovalTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long to wait for Executor's result before escalating; Executor reconciles unknown outcomes itself.</summary>
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan RecoveryRetryInterval { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>Reliability-weighted consensus (Phase 05); off until the verifier runs, when the walking skeleton's falsification check decides.</summary>
public sealed class ConsensusOptions
{
    public bool Enabled { get; set; }

    public IDictionary<string, double> SourceReliability { get; } = new Dictionary<string, double>(StringComparer.Ordinal);

    public double DefaultReliability { get; set; } = 0.8;

    public double PartialWeight { get; set; } = 0.6;

    public double ContradictionWeight { get; set; } = 1.2;

    public double AbstainBelowConfidence { get; set; } = 0.5;

    public int MinimumIndependentSupport { get; set; } = 2;

    public double DissentMargin { get; set; } = 0.25;
}

public sealed class DetectionRuleOptions
{
    public string Service { get; set; } = string.Empty;

    public string Metric { get; set; } = string.Empty;

    public double DetectionThreshold { get; set; }

    public double OnsetThreshold { get; set; } = 1.0;

    public int RequiredSamples { get; set; } = 2;

    public TimeSpan Lookback { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan ClearAfter { get; set; } = TimeSpan.FromMinutes(2);
}

public sealed class RecoveryCriterionOptions
{
    public string? Service { get; set; }

    public string Metric { get; set; } = string.Empty;

    public ThresholdComparison Comparison { get; set; } = ThresholdComparison.LessThan;

    public double Threshold { get; set; }
}
