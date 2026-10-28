using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Investigations;

/// <summary>Limits on one investigation (Phase 04 agent execution contract).</summary>
public sealed record InvestigationBudget(int MaxToolCalls, int MaxModelTokens, TimeSpan Timeout);

/// <summary>Runtime asks AgentWorker to investigate one question about an incident.</summary>
public sealed record InvestigateIncident : ISentinelMessage
{
    public static string MessageType => "agentworker.investigate-incident";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.AgentWorkerCommands;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required InvestigationId InvestigationId { get; init; }

    public required ServiceId AffectedServiceId { get; init; }

    public IReadOnlyList<ServiceId> RelatedServiceIds { get; init; } = [];

    /// <summary>What to determine, for example the most likely cause of checkout's SLO burn.</summary>
    public required string Objective { get; init; }

    public required TimeRange ObservationWindow { get; init; }

    public required InvestigationBudget Budget { get; init; }

    /// <summary>When the incident's symptoms began in event time, as Runtime detected it.</summary>
    public DateTimeOffset? SymptomOnset { get; init; }

    /// <summary>The specialist role (<see cref="InvestigationRoles"/>); null is the generalist investigator.</summary>
    public string? Role { get; init; }

    /// <summary>For the <see cref="InvestigationRoles.Verifier"/>: the hypotheses to try to falsify.</summary>
    public IReadOnlyList<HypothesisFinding>? Hypotheses { get; init; }

    /// <summary>For the <see cref="InvestigationRoles.Verifier"/>: the evidence accepted so far.</summary>
    public IReadOnlyList<EvidenceFinding>? Evidence { get; init; }

    /// <summary>
    /// Remembered incidents of the same tenant and environment that resemble this one (Phase 07),
    /// with the features they share. Hints, not evidence: an investigator must confirm any of them
    /// with its tools before citing it.
    /// </summary>
    public IReadOnlyList<Incidents.SimilarIncident>? SimilarIncidents { get; init; }
}

/// <summary>Specialist roles Runtime can assign (Phase 04) and the adversarial verifier (Phase 05).</summary>
public static class InvestigationRoles
{
    public const string Generalist = "generalist";
    public const string Metrics = "metrics";
    public const string Logs = "logs";
    public const string Traces = "traces";
    public const string Changes = "changes";
    public const string Topology = "topology";
    public const string Knowledge = "knowledge";
    public const string Verifier = "verifier";

    public static readonly IReadOnlyList<string> Specialists = [Metrics, Logs, Traces, Changes, Topology, Knowledge];

    public static bool IsKnown(string? role) => role is null || role == Generalist || role == Verifier || Specialists.Contains(role);
}

public enum EvidenceDirection
{
    Supports,
    Contradicts,
    Neutral
}

/// <summary>
/// A claim backed by specific observations and the tool call that produced it. Runtime decides
/// whether it becomes accepted evidence (architecture §15).
/// </summary>
public sealed record EvidenceFinding
{
    public required EvidenceId EvidenceId { get; init; }

    public required string Statement { get; init; }

    public required EvidenceDirection Direction { get; init; }

    /// <summary>Strength from 0 to 1.</summary>
    public required double Strength { get; init; }

    public required IReadOnlyList<ObservationId> SourceObservationIds { get; init; }

    public IReadOnlyList<HypothesisId> HypothesisIds { get; init; } = [];

    public required TimeRange EventTimeRange { get; init; }

    public required string ToolCallId { get; init; }

    /// <summary>Findings derived from the same underlying signal share a group, so they are counted once (Phase 05).</summary>
    public string? IndependenceGroup { get; init; }

    /// <summary>
    /// True when the query behind the evidence was incomplete, truncated or not observed through its
    /// range (ADR-007). Written only when true, so complete evidence keeps its schema-1 wire form.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Partial { get; init; }
}

public sealed record HypothesisFinding
{
    public required HypothesisId HypothesisId { get; init; }

    public required string Statement { get; init; }

    /// <summary>The proposed chain from cause to symptom, in order.</summary>
    public required IReadOnlyList<string> CausalChain { get; init; }

    public ServiceId? SuspectedServiceId { get; init; }

    public ObservationId? SuspectedChangeObservationId { get; init; }

    public IReadOnlyList<EvidenceId> SupportingEvidenceIds { get; init; } = [];

    public IReadOnlyList<EvidenceId> ContradictingEvidenceIds { get; init; } = [];

    /// <summary>
    /// The explanation's stable identity on the blackboard (Phase 04): investigators that propose the
    /// same key propose the same explanation, and Runtime merges them. A key with a <c>:</c> refines
    /// its prefix, for example <c>redis-saturation:reporting-worker</c> refines <c>redis-saturation</c>.
    /// </summary>
    public string? Key { get; init; }
}

/// <summary>The hypothesis key vocabulary investigators share, so specialists' explanations merge (Phase 04).</summary>
public static class HypothesisKeys
{
    public static string Change(ObservationId changeObservationId) => $"change:{changeObservationId}";

    public static string RedisSaturation(ServiceId? culprit = null) => culprit is { } service ? $"redis-saturation:{service}" : "redis-saturation";

    public static string DependencyRegression(ServiceId dependency) => $"dependency-regression:{dependency}";

    public static string LoadChange(ServiceId service) => $"load-change:{service}";

    public const string ExternalDependency = "external-dependency";

    /// <summary>The key this key refines, or null for a top-level key.</summary>
    public static string? Parent(string? key)
    {
        int separator = key?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        return separator > 0 && !key!.StartsWith("change:", StringComparison.Ordinal) ? key[..separator] : null;
    }
}

public sealed record ModelMetadata(string Provider, string Model, string? ModelVersion, string PromptVersion);

public enum ChallengeVerdict
{
    Survived,
    Falsified,
    Inconclusive
}

/// <summary>
/// One adversarial test of a hypothesis (Phase 05): what was asked, what the evidence said and the
/// evidence in the same report that decided it.
/// </summary>
public sealed record ChallengeFinding
{
    public required HypothesisId HypothesisId { get; init; }

    /// <summary>The falsification question, for example <c>change-serving-before-onset</c>.</summary>
    public required string Falsifier { get; init; }

    public required ChallengeVerdict Verdict { get; init; }

    public required string Detail { get; init; }

    public IReadOnlyList<EvidenceId> EvidenceIds { get; init; } = [];
}

public enum InvestigationOutcome
{
    Completed,
    Abstained,
    Failed
}

/// <summary>AgentWorker finished an investigation. Findings are proposals until Runtime accepts them.</summary>
public sealed record InvestigationCompleted : ISentinelMessage
{
    public static string MessageType => "agentworker.investigation.completed";

    public static int SchemaVersion => 1;

    public static string Stream => SentinelStreams.Investigations;

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required InvestigationId InvestigationId { get; init; }

    public required AgentExecutionId AgentExecutionId { get; init; }

    public required InvestigationOutcome Outcome { get; init; }

    public IReadOnlyList<EvidenceFinding> Evidence { get; init; } = [];

    public IReadOnlyList<HypothesisFinding> Hypotheses { get; init; } = [];

    public string? AbstentionReason { get; init; }

    public required ModelMetadata Model { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public string? Role { get; init; }

    /// <summary>The verifier's challenges; null for other roles.</summary>
    public IReadOnlyList<ChallengeFinding>? Challenges { get; init; }

    /// <summary>What the investigator could not determine and would need, for example a missing signal.</summary>
    public IReadOnlyList<string>? Unknowns { get; init; }

    /// <summary>The version of the tool catalog the investigator could call, for the run's version manifest (Phase 07).</summary>
    public string? ToolCatalogVersion { get; init; }
}
