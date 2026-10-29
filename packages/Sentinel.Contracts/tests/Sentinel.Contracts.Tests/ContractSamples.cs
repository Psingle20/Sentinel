using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Tests;

/// <summary>
/// One deterministic sample per message, modelled on the golden scenario: a checkout-service
/// deployment leaks Redis connections and is rolled back.
/// </summary>
internal static class ContractSamples
{
    private static readonly TenantId Tenant = TenantId.From("acme");
    private static readonly EnvironmentId Environment = EnvironmentId.From("reference");
    private static readonly ServiceId Checkout = ServiceId.From("checkout-service");
    private static readonly IncidentId Incident = IncidentId.From(Guid.Parse("0192f0c4-0000-7000-8000-000000000001"));
    private static readonly InvestigationId Investigation = InvestigationId.From(Guid.Parse("0192f0c4-0000-7000-8000-000000000002"));
    private static readonly HypothesisId LeakHypothesis = HypothesisId.From(Guid.Parse("0192f0c4-0000-7000-8000-000000000003"));
    private static readonly HypothesisId LoadHypothesis = HypothesisId.From(Guid.Parse("0192f0c4-0000-7000-8000-000000000004"));
    private static readonly EvidenceId ConnectionsEvidence = EvidenceId.From(Guid.Parse("0192f0c4-0000-7000-8000-000000000005"));
    private static readonly PlanId Plan = PlanId.From(Guid.Parse("0192f0c4-0000-7000-8000-000000000006"));
    private static readonly ActionId Rollback = ActionId.From(Guid.Parse("0192f0c4-0000-7000-8000-000000000007"));
    private static readonly DecisionId Decision = DecisionId.From(Guid.Parse("0192f0c4-0000-7000-8000-000000000008"));
    private static readonly AgentExecutionId Execution = AgentExecutionId.From(Guid.Parse("0192f0c4-0000-7000-8000-000000000009"));
    private static readonly ObservationId BurnRate = ObservationId.From("prometheus:checkout:slo-burn-rate:1793093100");
    private static readonly ObservationId Deployment = ObservationId.From("deploy-controller:checkout-service:v2.4.1");
    private static readonly DateTimeOffset Opened = new(2026, 10, 28, 9, 5, 0, TimeSpan.Zero);

    internal static readonly RecoveryCriterion BurnRateRecovered = new()
    {
        ServiceId = Checkout,
        MetricName = "slo_burn_rate",
        Comparison = ThresholdComparison.LessThan,
        Threshold = 1.0
    };

    internal static readonly RemediationPlan RollbackPlan = new()
    {
        PlanId = Plan,
        PlanVersion = 1,
        PlanHash = "sha256:6f1c2b9e4d7a3f0e8b5c1a9d2e4f6a8b0c3d5e7f9a1b3c5d7e9f0a2b4c6d8e0f",
        HypothesisId = LeakHypothesis,
        Actions = [new RollbackAction { ActionId = Rollback, TargetServiceId = Checkout, FromRevision = "v2.4.1", ToRevision = "v2.4.0" }],
        ExpectedRecovery = [BurnRateRecovered],
        ObservationWindow = TimeSpan.FromSeconds(90),
        Risk = RiskLevel.Medium
    };

    internal static readonly IReadOnlyDictionary<string, object> All = new Dictionary<string, object>
    {
        [ObservationRecorded.MessageType] = new ObservationRecorded(new MetricObservation
        {
            ObservationId = BurnRate,
            TenantId = Tenant,
            EnvironmentId = Environment,
            ServiceId = Checkout,
            SourceProvider = "prometheus",
            SourceResource = "slo:checkout_success:burn_rate5m",
            EventTime = Opened,
            IngestTime = Opened.AddSeconds(2),
            MetricName = "slo_burn_rate",
            Value = 14.8,
            Aggregation = "rate",
            Window = TimeSpan.FromMinutes(5),
            Labels = new Dictionary<string, string> { ["slo"] = "checkout-success" }
        }),
        [IncidentOpened.MessageType] = new IncidentOpened
        {
            TenantId = Tenant,
            EnvironmentId = Environment,
            IncidentId = Incident,
            AffectedServiceId = Checkout,
            Title = "checkout-service SLO burn",
            Severity = Severity.Critical,
            OpenedAt = Opened,
            TriggerObservationIds = [BurnRate],
            LinkedDeploymentObservationIds = [Deployment],
            StreamVersion = 1
        },
        [IncidentStatusChanged.MessageType] = new IncidentStatusChanged
        {
            TenantId = Tenant,
            EnvironmentId = Environment,
            IncidentId = Incident,
            Status = IncidentStatus.AwaitingApproval,
            Reason = "Rollback plan requires approval.",
            ChangedAt = Opened.AddMinutes(3),
            StreamVersion = 6
        },
        [IncidentBlackboardUpdated.MessageType] = new IncidentBlackboardUpdated
        {
            TenantId = Tenant,
            EnvironmentId = Environment,
            IncidentId = Incident,
            InvestigationId = Investigation,
            Role = InvestigationRoles.Metrics,
            AcceptedEvidence = 4,
            RejectedEvidence = 1,
            HypothesisKeys = ["change:deploy-controller:deployment:d1:started", "redis-saturation:reporting-worker"],
            UpdatedAt = Opened.AddMinutes(2),
            StreamVersion = 5
        },
        [InvestigateIncident.MessageType] = new InvestigateIncident
        {
            TenantId = Tenant,
            EnvironmentId = Environment,
            IncidentId = Incident,
            InvestigationId = Investigation,
            AffectedServiceId = Checkout,
            RelatedServiceIds = [ServiceId.From("order-service")],
            Objective = "Determine the most likely cause of checkout-service SLO burn.",
            ObservationWindow = new TimeRange(Opened.AddMinutes(-15), Opened),
            Budget = new InvestigationBudget(20, 40_000, TimeSpan.FromMinutes(2))
        },
        [InvestigationCompleted.MessageType] = new InvestigationCompleted
        {
            TenantId = Tenant,
            EnvironmentId = Environment,
            IncidentId = Incident,
            InvestigationId = Investigation,
            AgentExecutionId = Execution,
            Outcome = InvestigationOutcome.Completed,
            Evidence =
            [
                new EvidenceFinding
                {
                    EvidenceId = ConnectionsEvidence,
                    Statement = "checkout-service Redis client connections grew from 40 to 9,800 after v2.4.1 deployed.",
                    Direction = EvidenceDirection.Supports,
                    Strength = 0.9,
                    SourceObservationIds = [ObservationId.From("prometheus:redis:connected-clients:checkout:1793093100")],
                    HypothesisIds = [LeakHypothesis],
                    EventTimeRange = new TimeRange(Opened.AddMinutes(-6), Opened),
                    ToolCallId = "call_metric_range_01",
                    IndependenceGroup = "redis-connections"
                }
            ],
            Hypotheses =
            [
                new HypothesisFinding
                {
                    HypothesisId = LeakHypothesis,
                    Statement = "checkout-service v2.4.1 introduced a Redis connection leak.",
                    CausalChain = ["deployment v2.4.1", "connection leak", "Redis saturation", "checkout latency", "SLO burn"],
                    SuspectedServiceId = Checkout,
                    SuspectedChangeObservationId = Deployment,
                    SupportingEvidenceIds = [ConnectionsEvidence]
                },
                new HypothesisFinding
                {
                    HypothesisId = LoadHypothesis,
                    Statement = "A traffic spike saturated Redis.",
                    CausalChain = ["traffic spike", "Redis saturation", "SLO burn"]
                }
            ],
            Model = new ModelMetadata("meta", "muse-spark", null, "investigator-v1"),
            CompletedAt = Opened.AddMinutes(2)
        },
        [DecisionRecorded.MessageType] = new DecisionRecorded
        {
            TenantId = Tenant,
            EnvironmentId = Environment,
            IncidentId = Incident,
            DecisionId = Decision,
            Outcome = DecisionOutcome.Diagnosed,
            LeadingHypothesisId = LeakHypothesis,
            RejectedHypothesisIds = [LoadHypothesis],
            Confidence = 0.82,
            EvidenceCompleteness = 0.9,
            Calibration = Calibration.Uncalibrated,
            Rationale = "The leak precedes symptom onset and two independent observation families support it; request rate was flat.",
            DecidedAt = Opened.AddSeconds(150),
            StreamVersion = 5
        },
        [RemediationPlanProposed.MessageType] = new RemediationPlanProposed
        {
            TenantId = Tenant,
            EnvironmentId = Environment,
            IncidentId = Incident,
            Plan = RollbackPlan,
            PolicyEvaluation = new PolicyEvaluation(PolicyDecision.ApprovalRequired, "policy-2026.10.1", ["rollback.reference-environment: approval required"], Opened.AddSeconds(156)),
            StreamVersion = 6
        },
        [RecordApproval.MessageType] = new RecordApproval
        {
            TenantId = Tenant,
            EnvironmentId = Environment,
            IncidentId = Incident,
            PlanId = Plan,
            PlanVersion = 1,
            PlanHash = RollbackPlan.PlanHash,
            Attestation = "eyJhbGciOiJFUzI1NiIsImtpZCI6ImFwcHJvdmFsLTEifQ.eyJwbGFuIjoic2FtcGxlIn0.c2lnbmF0dXJl",
            ApproverId = "responder@example.test",
            ApprovedAt = Opened.AddMinutes(4)
        },
        [RecordRevocation.MessageType] = new RecordRevocation
        {
            TenantId = Tenant,
            EnvironmentId = Environment,
            IncidentId = Incident,
            PlanId = Plan,
            PlanVersion = 1,
            RevokedBy = "responder@example.test",
            Reason = "The rollback window closed.",
            RevokedAt = Opened.AddMinutes(5)
        },
        [RecordRejection.MessageType] = new RecordRejection
        {
            TenantId = Tenant,
            EnvironmentId = Environment,
            IncidentId = Incident,
            PlanId = Plan,
            PlanVersion = 1,
            RejectedBy = "responder@example.test",
            Reason = "Rollback conflicts with a scheduled migration.",
            RejectedAt = Opened.AddMinutes(4)
        },
        [ExecuteRemediation.MessageType] = new ExecuteRemediation
        {
            TenantId = Tenant,
            EnvironmentId = Environment,
            IncidentId = Incident,
            Plan = RollbackPlan,
            Attestation = "eyJhbGciOiJFUzI1NiIsImtpZCI6ImFwcHJvdmFsLTEifQ.eyJwbGFuIjoic2FtcGxlIn0.c2lnbmF0dXJl",
            FencingToken = 7,
            IdempotencyKey = "acme:0192f0c4-0000-7000-8000-000000000001:execute:1",
            RequestedAt = Opened.AddSeconds(246)
        },
        [RemediationExecuted.MessageType] = new RemediationExecuted
        {
            TenantId = Tenant,
            EnvironmentId = Environment,
            IncidentId = Incident,
            PlanId = Plan,
            PlanVersion = 1,
            Outcome = ExecutionOutcome.ActionVerified,
            Detail = "checkout-service is serving v2.4.0.",
            CompletedAt = Opened.AddMinutes(5)
        },
        [RecoveryEvaluated.MessageType] = new RecoveryEvaluated
        {
            TenantId = Tenant,
            EnvironmentId = Environment,
            IncidentId = Incident,
            PlanId = Plan,
            Outcome = RecoveryOutcome.Recovered,
            Measurements = [new RecoveryMeasurement(BurnRateRecovered, 0.4, true)],
            ObservedThrough = Opened.AddSeconds(390),
            EvaluatedAt = Opened.AddSeconds(396),
            StreamVersion = 10
        },

        // Logs and traces travel only in query answers (ADR-018), so the query contract carries their compatibility snapshot.
        [QueryAnswer] = new ObservationQueryResult(
            [
                new LogObservation
                {
                    ObservationId = ObservationId.From("loki:service-warnings:checkout-service:1793093040:9f2c"),
                    TenantId = Tenant,
                    EnvironmentId = Environment,
                    ServiceId = Checkout,
                    SourceProvider = "loki",
                    SourceResource = "{service=\"checkout-service\"}",
                    EventTime = Opened.AddSeconds(-20),
                    IngestTime = Opened.AddSeconds(-18),
                    Severity = LogSeverity.Error,
                    Message = "Checkout cart-17526 failed on Redis: ERR max number of clients reached",
                    ExceptionType = "Reference.Common.Redis.RedisUnavailableException",
                    Attributes = new Dictionary<string, string> { ["signature"] = "Checkout cart-<n> failed on Redis: ERR max number of clients reached" }
                },
                new TraceObservation
                {
                    ObservationId = ObservationId.From("jaeger:4bf92f3577b34da6:00f067aa0ba902b7"),
                    TenantId = Tenant,
                    EnvironmentId = Environment,
                    ServiceId = Checkout,
                    SourceProvider = "jaeger",
                    SourceResource = "traces",
                    EventTime = Opened.AddSeconds(-15),
                    IngestTime = Opened.AddSeconds(-10),
                    TraceId = "4bf92f3577b34da6a3ce929d0e0e4736",
                    SpanId = "00f067aa0ba902b7",
                    OperationName = "POST /api/orders",
                    Duration = TimeSpan.FromMilliseconds(842),
                    Status = SpanStatus.Error,
                    DependencyServiceId = ServiceId.From("order-service")
                },
                new DeploymentObservation
                {
                    ObservationId = Deployment,
                    TenantId = Tenant,
                    EnvironmentId = Environment,
                    ServiceId = Checkout,
                    SourceProvider = "deploy-controller",
                    SourceResource = "deployments/d1",
                    EventTime = Opened.AddSeconds(-240),
                    IngestTime = Opened.AddSeconds(-238),
                    DeploymentId = "d1",
                    Version = "v2.4.1",
                    PreviousVersion = "v2.4.0",
                    Actor = "release-pipeline",
                    StartedAt = Opened.AddSeconds(-260),
                    CompletedAt = Opened.AddSeconds(-240)
                },
                new TopologyObservation
                {
                    ObservationId = ObservationId.From("jaeger:topology:checkout-service:order-service"),
                    TenantId = Tenant,
                    EnvironmentId = Environment,
                    ServiceId = Checkout,
                    SourceProvider = "jaeger",
                    SourceResource = "dependencies",
                    EventTime = Opened,
                    IngestTime = Opened.AddSeconds(5),
                    TargetServiceId = ServiceId.From("order-service"),
                    DependencyType = "http",
                    Protocol = "http",
                    Confidence = 0.95,
                    FirstSeen = Opened.AddHours(-6),
                    LastSeen = Opened
                }
            ],
            new QueryMetadata
            {
                ObservedThrough = Opened,
                Freshness = TimeSpan.FromSeconds(12),
                Completeness = QueryCompleteness.Partial,
                Truncated = true,
                ProviderHealth = [new ProviderHealth("loki", ProviderStatus.Healthy), new ProviderHealth("jaeger", ProviderStatus.Degraded)],
                RedactionPolicyVersion = "redaction-v1",
                ContinuationToken = "1793093100.prometheus:checkout:slo-burn-rate:1793093100"
            })
    };

    /// <summary>The snapshot name of Ingestion's observation query answer, which is a contract but not a message.</summary>
    internal const string QueryAnswer = "ingestion.query.observations.result";

    /// <summary>Samples of contracts that are not messages, which the message rules leave out.</summary>
    internal static readonly IReadOnlyList<string> NonMessageContracts = [QueryAnswer];
}
