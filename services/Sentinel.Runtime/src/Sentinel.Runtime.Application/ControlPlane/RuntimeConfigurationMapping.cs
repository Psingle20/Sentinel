using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Runtime.Application.Options;

namespace Sentinel.Runtime.Application.ControlPlane;

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
                BatchServices = [.. options.BatchServices],
                QueueServices = new Dictionary<string, string>(options.QueueServices, StringComparer.Ordinal),
                RouteServices = new Dictionary<string, string>(options.RouteServices, StringComparer.Ordinal)
            },
            Suppressions = [.. options.Correlation.Suppressions.Select(rule => new SuppressionSetting(rule.Service, rule.Metric, rule.From, rule.Until, rule.Reason))],
            Investigation = new InvestigationSetting
            {
                Roles = [.. options.Investigation.Roles],
                Challenge = options.Investigation.Challenge,
                Escalation = options.Investigation.Escalation.Enabled,
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
                ReconciliationTimeout = options.Workflow.ReconciliationTimeout,
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
        options.QueueServices.Clear();
        foreach ((string service, string queue) in document.Catalog.QueueServices)
        {
            options.QueueServices[service] = queue;
        }

        options.RouteServices.Clear();
        foreach ((string service, string rule) in document.Catalog.RouteServices)
        {
            options.RouteServices[service] = rule;
        }

        Replace(options.Correlation.Suppressions, document.Suppressions.Select(rule => new SuppressionOptions { Service = rule.Service, Metric = rule.Metric, From = rule.From, Until = rule.Until, Reason = rule.Reason }));

        Replace(options.Investigation.Roles, document.Investigation.Roles);
        options.Investigation.Challenge = document.Investigation.Challenge;
        options.Investigation.Escalation.Enabled = document.Investigation.Escalation;
        options.Investigation.ReportGrace = document.Investigation.ReportGrace;
        options.InvestigationWindow = document.Investigation.Window;
        options.InvestigationTimeout = document.Investigation.Timeout;
        options.InvestigationMaxToolCalls = document.Investigation.MaxToolCalls;
        options.InvestigationMaxModelTokens = document.Investigation.MaxModelTokens;

        options.Workflow.ApprovalTimeout = document.Workflow.ApprovalTimeout;
        options.Workflow.ExecutionTimeout = document.Workflow.ExecutionTimeout;
        options.Workflow.ReconciliationTimeout = document.Workflow.ReconciliationTimeout;
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

        if (workflow.ReconciliationTimeout < TimeSpan.Zero || workflow.ReconciliationTimeout > TimeSpan.FromDays(1))
        {
            problems.Add("The reconciliation timeout is between zero, which escalates an unknown outcome at once, and 1 day.");
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
