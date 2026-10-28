using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Tests;

/// <summary>Builders for incidents shaped like the reference scenarios, in event time.</summary>
internal static class IncidentScenario
{
    public static readonly DateTimeOffset Start = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    public static readonly ServiceId Checkout = ServiceId.From("checkout-service");
    public static readonly ServiceId Reporting = ServiceId.From("reporting-worker");

    public static LinkedChange Deployment(string service, string version, string previous, int startedAt, int? servingAt, string id) => new()
    {
        DeploymentId = id,
        ServiceId = ServiceId.From(service),
        Version = version,
        PreviousVersion = previous,
        ChangeKind = "deploy",
        StartedAt = Start.AddSeconds(startedAt),
        CompletedAt = servingAt is { } serving ? Start.AddSeconds(serving) : null,
        ObservationIds = [ObservationId.From($"deploy-controller:deployment:{id}:started"), ObservationId.From($"deploy-controller:deployment:{id}:completed")]
    };

    /// <summary>An incident detected at <paramref name="onsetAt"/> seconds with an investigation requested.</summary>
    public static (Incident Incident, InvestigationId Investigation) Detected(int onsetAt, params LinkedChange[] changes)
    {
        var incident = Incident.Detect(
            IncidentId.New(),
            TenantId.From("acme"),
            EnvironmentId.From("reference"),
            Checkout,
            "checkout-service slo_burn_rate at 14.8",
            IncidentSeverity.Critical,
            Start.AddSeconds(onsetAt),
            [ObservationId.From("prometheus:slo_burn_rate:checkout-service:1")],
            changes,
            Start.AddSeconds(onsetAt + 10));
        var investigation = InvestigationId.New();
        incident.RequestInvestigation(investigation, "Find the cause.", Start.AddSeconds(onsetAt - 900), Start.AddSeconds(onsetAt + 10), Start.AddSeconds(onsetAt + 10));
        return (incident, investigation);
    }

    public static Evidence Supporting(HypothesisId hypothesis, double strength, int from, int to, params string[] observations) => new()
    {
        EvidenceId = EvidenceId.New(),
        Statement = "A signal.",
        Direction = EvidenceDirection.Supports,
        Strength = strength,
        SourceObservationIds = [.. observations.Select(ObservationId.From)],
        HypothesisIds = [hypothesis],
        From = Start.AddSeconds(from),
        To = Start.AddSeconds(to),
        ToolCallId = "call_1"
    };

    public static Evidence Contradicting(HypothesisId hypothesis, double strength, params string[] observations) =>
        Supporting(hypothesis, strength, 0, 10, observations) with { Direction = EvidenceDirection.Contradicts };

    public static Hypothesis Hypothesis(HypothesisId id, string statement, ServiceId? service = null, ObservationId? change = null) => new()
    {
        HypothesisId = id,
        Statement = statement,
        CausalChain = ["cause", "symptom"],
        SuspectedServiceId = service,
        SuspectedChangeObservationId = change
    };

    public static InvestigationReport Report(InvestigationId investigation, IReadOnlyList<Evidence> evidence, params Hypothesis[] hypotheses) => new()
    {
        InvestigationId = investigation,
        AgentExecutionId = AgentExecutionId.New(),
        Result = InvestigationResult.Completed,
        Evidence = evidence,
        Hypotheses = hypotheses,
        ModelProvider = "sentinel-test-double",
        Model = "deterministic-analyst",
        PromptVersion = "investigator-v1",
        CompletedAt = Start.AddSeconds(200)
    };

    public static ProposedPlan Plan(HypothesisId hypothesis, PolicyVerdict policy = PolicyVerdict.ApprovalRequired) => new()
    {
        PlanId = PlanId.New(),
        Version = 1,
        Hash = "sha256:plan",
        HypothesisId = hypothesis,
        TargetServiceId = Checkout,
        ObservationWindow = TimeSpan.FromSeconds(90),
        Document = "{}",
        Policy = policy,
        PolicyVersion = "static-2026.10.1",
        ProposedAt = Start.AddSeconds(210)
    };
}

/// <summary>Captures enqueued messages instead of storing them.</summary>
internal sealed class RecordingOutbox : IOutbox
{
    public List<(object Message, MessageMetadata? Metadata)> Messages { get; } = [];

    public void Enqueue<TMessage>(TMessage message, MessageMetadata? metadata = null)
        where TMessage : class => Messages.Add((message, metadata));

    public IEnumerable<TMessage> Of<TMessage>() => Messages.Select(entry => entry.Message).OfType<TMessage>();
}
