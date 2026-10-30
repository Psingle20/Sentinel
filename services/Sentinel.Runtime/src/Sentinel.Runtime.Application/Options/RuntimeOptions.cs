using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.Options;

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

    /// <summary>Services that consume a queue, and the queue: a poison message they cannot process is remediated by quarantining it (Phase E01).</summary>
    public IDictionary<string, string> QueueServices { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The service catalog's owning team per service.</summary>
    public IDictionary<string, string> Owners { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public CorrelationOptions Correlation { get; } = new();

    public InvestigationPlanOptions Investigation { get; } = new();

    public WorkflowOptions Workflow { get; } = new();

    public ConsensusOptions Consensus { get; } = new();

    public MemoryOptions Memory { get; } = new();

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
