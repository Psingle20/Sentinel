using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

public sealed record IncidentDetail
{
    public required IncidentSummary Summary { get; init; }

    public IReadOnlyList<ObservationId> TriggerObservationIds { get; init; } = [];

    public IReadOnlyList<ObservationId> LinkedDeploymentObservationIds { get; init; } = [];

    public IReadOnlyList<TimelineEntry> Timeline { get; init; } = [];

    public IReadOnlyList<InvestigationView> Investigations { get; init; } = [];

    public IReadOnlyList<EvidenceView> Evidence { get; init; } = [];

    public IReadOnlyList<HypothesisView> Hypotheses { get; init; } = [];

    public DecisionView? Decision { get; init; }

    public PlanView? Plan { get; init; }

    public ExecutionView? Execution { get; init; }

    public RecoveryView? Recovery { get; init; }

    /// <summary>The affected service and every related service whose symptom was merged in.</summary>
    public IReadOnlyList<ServiceId> AffectedServices { get; init; } = [];

    public IReadOnlyList<LinkedChangeView> Changes { get; init; } = [];

    public IReadOnlyList<LinkedSignalView> LinkedSignals { get; init; } = [];

    public IReadOnlyList<ChallengeView> Challenges { get; init; } = [];

    /// <summary>Every decision of the current cycle, oldest first, superseded ones included.</summary>
    public IReadOnlyList<DecisionView> Decisions { get; init; } = [];

    /// <summary>How many times the incident was reopened.</summary>
    public int Cycle { get; init; }

    public CausalGraphView? CausalGraph { get; init; }
}
