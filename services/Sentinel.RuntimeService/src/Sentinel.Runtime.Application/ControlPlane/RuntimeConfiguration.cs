using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;

namespace Sentinel.Runtime.Application.ControlPlane;

/// <summary>
/// Runtime's control-plane document (Phase 08): the SLO detection rules, the recovery signals each
/// service must restore, the service catalog, maintenance suppressions, which investigators run,
/// the workflow's timers and the consensus thresholds. Published versions apply over the deployment
/// configuration; timers are captured when a workflow starts, so a change affects new cycles only.
/// </summary>
public sealed record RuntimeConfigurationDocument : IVersionedDocument
{
    public required string Version { get; init; }

    public IReadOnlyList<DetectionRuleSetting> Detection { get; init; } = [];

    public IReadOnlyDictionary<string, IReadOnlyList<RecoverySignalSetting>> Recovery { get; init; } = new Dictionary<string, IReadOnlyList<RecoverySignalSetting>>();

    public ServiceCatalogSetting Catalog { get; init; } = new();

    public IReadOnlyList<SuppressionSetting> Suppressions { get; init; } = [];

    public InvestigationSetting Investigation { get; init; } = new();

    public WorkflowSetting Workflow { get; init; } = new();

    public ConsensusSetting Consensus { get; init; } = new();
}

/// <summary>An SLO signal that opens an incident: above the detection threshold for the required samples.</summary>
public sealed record DetectionRuleSetting(string Service, string Metric, double DetectionThreshold, double OnsetThreshold, int RequiredSamples, TimeSpan Lookback, TimeSpan ClearAfter);

public sealed record RecoverySignalSetting(string? Service, string Metric, ThresholdComparison Comparison, double Threshold);

public sealed record ServiceCatalogSetting
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Dependencies { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyDictionary<string, string> Owners { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<string> BatchServices { get; init; } = [];
}

public sealed record SuppressionSetting(string? Service, string? Metric, DateTimeOffset From, DateTimeOffset Until, string Reason);

public sealed record InvestigationSetting
{
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>Runs the adversarial verifier before deciding (Phase 05).</summary>
    public bool Challenge { get; init; }

    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan ReportGrace { get; init; } = TimeSpan.FromSeconds(30);

    public int MaxToolCalls { get; init; } = 12;

    public int MaxModelTokens { get; init; } = 40_000;
}

public sealed record WorkflowSetting
{
    public TimeSpan ApprovalTimeout { get; init; } = TimeSpan.FromMinutes(30);

    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan ObservationWindow { get; init; } = TimeSpan.FromSeconds(90);

    public TimeSpan EvaluationLag { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan EvaluationTimeout { get; init; } = TimeSpan.FromMinutes(3);
}

public sealed record ConsensusSetting
{
    public bool Enabled { get; init; }

    public IReadOnlyDictionary<string, double> SourceReliability { get; init; } = new Dictionary<string, double>();

    public double DefaultReliability { get; init; } = 0.8;

    public double PartialWeight { get; init; } = 0.6;

    public double ContradictionWeight { get; init; } = 1.2;

    public double AbstainBelowConfidence { get; init; } = 0.5;

    public int MinimumIndependentSupport { get; init; } = 2;

    public double DissentMargin { get; init; } = 0.25;
}

/// <summary>Moves Runtime's options to and from the control-plane document.</summary>
public static class RuntimeConfigurationMapping
{
    /// <summary>The version name of the document derived from the deployment configuration.</summary>
    public const string DeploymentVersion = "deployment";

    public static RuntimeConfigurationDocument From(RuntimeOptions options, string version = DeploymentVersion)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new RuntimeConfigurationDocument
        {
            Version = version,
            Detection = [.. options.Detection.Select(rule => new DetectionRuleSetting(rule.Service, rule.Metric, rule.DetectionThreshold, rule.OnsetThreshold, rule.RequiredSamples, rule.Lookback, rule.ClearAfter))],
            Recovery = options.Recovery.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<RecoverySignalSetting>)[.. entry.Value.Select(criterion => new RecoverySignalSetting(criterion.Service, criterion.Metric, criterion.Comparison, criterion.Threshold))],
                StringComparer.Ordinal),
            Catalog = new ServiceCatalogSetting
            {
                Dependencies = options.Dependencies.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)[.. entry.Value], StringComparer.Ordinal),
                Owners = new Dictionary<string, string>(options.Owners, StringComparer.Ordinal),
                BatchServices = [.. options.BatchServices]
            },
            Suppressions = [.. options.Correlation.Suppressions.Select(rule => new SuppressionSetting(rule.Service, rule.Metric, rule.From, rule.Until, rule.Reason))],
            Investigation = new InvestigationSetting
            {
                Roles = [.. options.Investigation.Roles],
                Challenge = options.Investigation.Challenge,
                Window = options.InvestigationWindow,
                Timeout = options.InvestigationTimeout,
                ReportGrace = options.Investigation.ReportGrace,
                MaxToolCalls = options.InvestigationMaxToolCalls,
                MaxModelTokens = options.InvestigationMaxModelTokens
            },
            Workflow = new WorkflowSetting
            {
                ApprovalTimeout = options.Workflow.ApprovalTimeout,
                ExecutionTimeout = options.Workflow.ExecutionTimeout,
                ObservationWindow = options.ObservationWindow,
                EvaluationLag = options.EvaluationLag,
                EvaluationTimeout = options.EvaluationTimeout
            },
            Consensus = new ConsensusSetting
            {
                Enabled = options.Consensus.Enabled,
                SourceReliability = new Dictionary<string, double>(options.Consensus.SourceReliability, StringComparer.Ordinal),
                DefaultReliability = options.Consensus.DefaultReliability,
                PartialWeight = options.Consensus.PartialWeight,
                ContradictionWeight = options.Consensus.ContradictionWeight,
                AbstainBelowConfidence = options.Consensus.AbstainBelowConfidence,
                MinimumIndependentSupport = options.Consensus.MinimumIndependentSupport,
                DissentMargin = options.Consensus.DissentMargin
            }
        };
    }

    /// <summary>Replaces every setting the document manages; anything else stays as deployed.</summary>
    public static void Apply(RuntimeOptions options, RuntimeConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(document);

        Replace(options.Detection, document.Detection.Select(rule => new DetectionRuleOptions
        {
            Service = rule.Service,
            Metric = rule.Metric,
            DetectionThreshold = rule.DetectionThreshold,
            OnsetThreshold = rule.OnsetThreshold,
            RequiredSamples = rule.RequiredSamples,
            Lookback = rule.Lookback,
            ClearAfter = rule.ClearAfter
        }));
        options.Recovery.Clear();
        foreach ((string service, IReadOnlyList<RecoverySignalSetting> criteria) in document.Recovery)
        {
            options.Recovery[service] = [.. criteria.Select(criterion => new RecoveryCriterionOptions { Service = criterion.Service, Metric = criterion.Metric, Comparison = criterion.Comparison, Threshold = criterion.Threshold })];
        }

        options.Dependencies.Clear();
        foreach ((string service, IReadOnlyList<string> dependencies) in document.Catalog.Dependencies)
        {
            options.Dependencies[service] = [.. dependencies];
        }

        options.Owners.Clear();
        foreach ((string service, string owner) in document.Catalog.Owners)
        {
            options.Owners[service] = owner;
        }

        Replace(options.BatchServices, document.Catalog.BatchServices);
        Replace(options.Correlation.Suppressions, document.Suppressions.Select(rule => new SuppressionOptions { Service = rule.Service, Metric = rule.Metric, From = rule.From, Until = rule.Until, Reason = rule.Reason }));

        Replace(options.Investigation.Roles, document.Investigation.Roles);
        options.Investigation.Challenge = document.Investigation.Challenge;
        options.Investigation.ReportGrace = document.Investigation.ReportGrace;
        options.InvestigationWindow = document.Investigation.Window;
        options.InvestigationTimeout = document.Investigation.Timeout;
        options.InvestigationMaxToolCalls = document.Investigation.MaxToolCalls;
        options.InvestigationMaxModelTokens = document.Investigation.MaxModelTokens;

        options.Workflow.ApprovalTimeout = document.Workflow.ApprovalTimeout;
        options.Workflow.ExecutionTimeout = document.Workflow.ExecutionTimeout;
        options.ObservationWindow = document.Workflow.ObservationWindow;
        options.EvaluationLag = document.Workflow.EvaluationLag;
        options.EvaluationTimeout = document.Workflow.EvaluationTimeout;

        options.Consensus.Enabled = document.Consensus.Enabled;
        options.Consensus.SourceReliability.Clear();
        foreach ((string source, double reliability) in document.Consensus.SourceReliability)
        {
            options.Consensus.SourceReliability[source] = reliability;
        }

        options.Consensus.DefaultReliability = document.Consensus.DefaultReliability;
        options.Consensus.PartialWeight = document.Consensus.PartialWeight;
        options.Consensus.ContradictionWeight = document.Consensus.ContradictionWeight;
        options.Consensus.AbstainBelowConfidence = document.Consensus.AbstainBelowConfidence;
        options.Consensus.MinimumIndependentSupport = document.Consensus.MinimumIndependentSupport;
        options.Consensus.DissentMargin = document.Consensus.DissentMargin;
    }

    // Options collections are get-only, so a document replaces their contents rather than the collection.
    private static void Replace<T>(IList<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (T item in items)
        {
            target.Add(item);
        }
    }

    /// <summary>Why the document cannot run; empty when it can.</summary>
    public static IReadOnlyList<string> Problems(RuntimeConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var problems = new List<string>();
        if (document.Detection.Count == 0)
        {
            problems.Add("At least one detection rule is required.");
        }

        foreach (DetectionRuleSetting rule in document.Detection)
        {
            string name = $"Detection rule {rule.Service}/{rule.Metric}";
            if (string.IsNullOrWhiteSpace(rule.Service) || string.IsNullOrWhiteSpace(rule.Metric))
            {
                problems.Add("Every detection rule names a service and a metric.");
            }

            if (!double.IsFinite(rule.DetectionThreshold) || !double.IsFinite(rule.OnsetThreshold) || rule.DetectionThreshold <= rule.OnsetThreshold)
            {
                problems.Add($"{name} needs a finite detection threshold above its onset threshold.");
            }

            if (rule.RequiredSamples is < 1 or > 100 || rule.Lookback <= TimeSpan.Zero || rule.ClearAfter <= TimeSpan.Zero)
            {
                problems.Add($"{name} needs 1 to 100 required samples and positive lookback and clear-after times.");
            }
        }

        if (document.Detection.GroupBy(rule => (rule.Service, rule.Metric)).Any(group => group.Count() > 1))
        {
            problems.Add("A service and metric can have only one detection rule.");
        }

        if (document.Recovery.Values.SelectMany(criteria => criteria).Any(criterion => string.IsNullOrWhiteSpace(criterion.Metric) || !double.IsFinite(criterion.Threshold)))
        {
            problems.Add("Every recovery signal names a metric and a finite threshold.");
        }

        if (document.Catalog.Dependencies.Any(entry => entry.Value.Contains(entry.Key, StringComparer.Ordinal)))
        {
            problems.Add("A service cannot depend on itself.");
        }

        if (document.Catalog.Owners.Any(entry => string.IsNullOrWhiteSpace(entry.Value)))
        {
            problems.Add("Every owner entry names a team.");
        }

        if (document.Suppressions.Any(rule => rule.Until <= rule.From || string.IsNullOrWhiteSpace(rule.Reason)))
        {
            problems.Add("Every suppression ends after it starts and gives a reason.");
        }

        InvestigationSetting investigation = document.Investigation;
        if (investigation.Roles.FirstOrDefault(role => !InvestigationRoles.IsKnown(role)) is { } unknown)
        {
            problems.Add($"'{unknown}' is not an investigator role.");
        }

        if (investigation.Timeout <= TimeSpan.Zero || investigation.Timeout > TimeSpan.FromMinutes(30) || investigation.ReportGrace < TimeSpan.Zero || investigation.Window <= TimeSpan.Zero)
        {
            problems.Add("The investigation timeout is between zero and 30 minutes, with a positive window and a non-negative report grace.");
        }

        if (investigation.MaxToolCalls is < 1 or > 100 || investigation.MaxModelTokens is < 1_000 or > 1_000_000)
        {
            problems.Add("An investigation allows 1 to 100 tool calls and 1,000 to 1,000,000 model tokens.");
        }

        WorkflowSetting workflow = document.Workflow;
        if (workflow.ApprovalTimeout < TimeSpan.FromMinutes(1) || workflow.ApprovalTimeout > TimeSpan.FromDays(1) || workflow.ExecutionTimeout <= TimeSpan.Zero)
        {
            problems.Add("The approval timeout is between 1 minute and 1 day, and the execution timeout is positive.");
        }

        if (workflow.ObservationWindow < TimeSpan.FromSeconds(10) || workflow.ObservationWindow > TimeSpan.FromHours(1) || workflow.EvaluationLag < TimeSpan.Zero || workflow.EvaluationTimeout <= workflow.EvaluationLag)
        {
            problems.Add("The recovery observation window is between 10 seconds and 1 hour, and the evaluation timeout exceeds the evaluation lag.");
        }

        ConsensusSetting consensus = document.Consensus;
        if (consensus.SourceReliability.Values.Append(consensus.DefaultReliability).Any(value => value is < 0 or > 1)
            || consensus.AbstainBelowConfidence is < 0 or > 1
            || consensus.DissentMargin is < 0 or > 1)
        {
            problems.Add("Source reliabilities, the abstention threshold and the dissent margin are between 0 and 1.");
        }

        if (consensus.PartialWeight is <= 0 or > 1 || consensus.ContradictionWeight <= 0 || consensus.MinimumIndependentSupport < 1)
        {
            problems.Add("The partial-evidence weight is above 0 and at most 1, the contradiction weight is positive, and at least one independent source is required.");
        }

        return problems;
    }
}

/// <summary>Reads and publishes Runtime's control-plane document; the deployment configuration seeds it.</summary>
public sealed class RuntimeConfigurationService(VersionedDocumentPublisher<RuntimeConfigurationDocument> publisher, RuntimeDeploymentOptions deployment)
{
    public Task<DocumentState<RuntimeConfigurationDocument>> CurrentAsync(CancellationToken cancellationToken) =>
        publisher.CurrentAsync(Seed, cancellationToken);

    public Task<IReadOnlyList<DocumentVersion>> HistoryAsync(CancellationToken cancellationToken) =>
        publisher.HistoryAsync(Seed, 100, cancellationToken);

    public Task<Result<PublishedDocument<RuntimeConfigurationDocument>>> PublishAsync(RuntimeConfigurationDocument document, string expectedVersion, string author, CancellationToken cancellationToken) =>
        publisher.PublishAsync(document, expectedVersion, author, Seed, Validate, cancellationToken);

    private RuntimeConfigurationDocument Seed() => RuntimeConfigurationMapping.From(deployment.Value);

    // The document's own rules, then Runtime's startup rules on the options it would produce.
    private IReadOnlyList<string> Validate(RuntimeConfigurationDocument document)
    {
        IReadOnlyList<string> problems = RuntimeConfigurationMapping.Problems(document);
        if (problems.Count > 0)
        {
            return problems;
        }

        RuntimeOptions candidate = deployment.Create();
        RuntimeConfigurationMapping.Apply(candidate, document);
        return candidate.IsValid() ? [] : ["The configuration does not satisfy Runtime's startup rules."];
    }
}

/// <summary>Runtime's options exactly as deployed, before any control-plane document is applied.</summary>
public sealed class RuntimeDeploymentOptions(Func<RuntimeOptions> create)
{
    public RuntimeOptions Value => create();

    /// <summary>A fresh copy to apply a candidate document to.</summary>
    public RuntimeOptions Create() => create();
}
