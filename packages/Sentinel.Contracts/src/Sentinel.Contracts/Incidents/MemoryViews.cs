using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// The versions a run was made with (Phase 07): enough to tell whether two runs are comparable and
/// to select the same model, prompt, tools, algorithm and policy again for a replay.
/// </summary>
public sealed record VersionManifest
{
    /// <summary>The incident-memory and run-record schema version.</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>The decision algorithm, for example <c>consensus-v1</c>.</summary>
    public string? DecisionAlgorithm { get; init; }

    /// <summary>Every model that reported, with its prompt version, in first-report order.</summary>
    public IReadOnlyList<ModelMetadata> Models { get; init; } = [];

    /// <summary>The investigation tool catalog the investigators used.</summary>
    public IReadOnlyList<string> ToolCatalogVersions { get; init; } = [];

    public string? PolicyVersion { get; init; }

    /// <summary>The redaction policy applied to the observations behind the evidence.</summary>
    public string? RedactionPolicyVersion { get; init; }

    /// <summary>The scenario package or dataset version, when the run was an evaluation run.</summary>
    public string? Dataset { get; init; }
}

/// <summary>How long incident memory is kept and who may see it (Phase 07 retention and privacy metadata).</summary>
public sealed record RetentionMetadata(
    DateTimeOffset RetainUntil,
    bool LegalHold,
    string PrivacyClass,
    string RedactionPolicyVersion,
    DateTimeOffset? DeletedAt = null,
    string? DeletedBy = null,
    string? DeletionReason = null);

/// <summary>A symptom the incident was opened or merged for: which service, which signal.</summary>
public sealed record SymptomSignature(ServiceId ServiceId, string MetricName);

/// <summary>What the incident concluded was the cause, if it concluded anything.</summary>
public sealed record MemoryCause(string? Key, string Statement, ServiceId? ServiceId, double Confidence, string? ChangeDeploymentId);

/// <summary>What was done about the incident and how it ended.</summary>
public sealed record MemoryAction(string Kind, ServiceId TargetServiceId, string Summary, string PolicyDecision, string? ExecutionOutcome, string? RecoveryOutcome);

/// <summary>
/// One incident remembered (Phase 07): its symptoms, topology, changes, evidence, hypotheses,
/// decision, actions and outcome, linked by ID to the authoritative stream it was derived from.
/// Purged memory keeps only its identity and retention metadata.
/// </summary>
public sealed record IncidentMemoryView
{
    public required IncidentId IncidentId { get; init; }

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required string Title { get; init; }

    public required IncidentStatus Status { get; init; }

    public required DateTimeOffset OpenedAt { get; init; }

    public DateTimeOffset? ClosedAt { get; init; }

    public required int Cycle { get; init; }

    /// <summary>The stream version the record was derived from.</summary>
    public required long StreamVersion { get; init; }

    public IReadOnlyList<ServiceId> AffectedServices { get; init; } = [];

    public IReadOnlyList<SymptomSignature> Symptoms { get; init; } = [];

    public IReadOnlyList<LinkedChangeView> Changes { get; init; } = [];

    public IReadOnlyList<string> HypothesisKeys { get; init; } = [];

    public IReadOnlyList<EvidenceId> EvidenceIds { get; init; } = [];

    public IReadOnlyList<ObservationId> ObservationIds { get; init; } = [];

    public DecisionOutcome? Decision { get; init; }

    public MemoryCause? Cause { get; init; }

    public MemoryAction? Action { get; init; }

    public required VersionManifest Versions { get; init; }

    public required RetentionMetadata Retention { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>The features similar-incident retrieval matches on, for example <c>service:checkout-service</c>.</summary>
    public IReadOnlyList<string> Features { get; init; } = [];
}

/// <summary>
/// A remembered incident that resembles another (Phase 07), with the features they share as
/// provenance. A hint for investigators, never evidence: it cites no observation of this incident.
/// </summary>
public sealed record SimilarIncident(
    IncidentId IncidentId,
    double Similarity,
    IReadOnlyList<string> SharedFeatures,
    string? CauseKey,
    string? CauseStatement,
    string Outcome,
    DateTimeOffset RecordedAt);

/// <summary>Replay levels (Phase 07): which one ran decides what a result may claim.</summary>
public enum ReplayLevel
{
    /// <summary>Level 1: rebuild state and projections from the event stream, and re-run recorded decisions.</summary>
    Domain,

    /// <summary>Level 2: return captured tool and provider results to reproduce the reasoning inputs.</summary>
    Investigation,

    /// <summary>Level 3: rerun a selected model and prompt version against frozen inputs.</summary>
    ModelReevaluation,

    /// <summary>Level 4: rerun the failure against the reference environment (the scenario runner).</summary>
    SystemSimulation
}

/// <summary>A recorded decision re-run on the stream prefix it was made from.</summary>
public sealed record DecisionReplay(
    DecisionId DecisionId,
    long StreamVersion,
    string? AlgorithmVersion,
    bool SettingsRecorded,
    DecisionOutcome RecordedOutcome,
    DecisionOutcome ReplayedOutcome,
    HypothesisId? RecordedLeader,
    HypothesisId? ReplayedLeader,
    double RecordedConfidence,
    double ReplayedConfidence,
    string? RecordedInputsHash,
    string? ReplayedInputsHash,
    bool Matches);

/// <summary>The result of a level-1 domain replay. Read-only: nothing is written, sent or executed.</summary>
public sealed record DomainReplayResult
{
    public ReplayLevel Level { get; init; } = ReplayLevel.Domain;

    /// <summary>Domain replay is deterministic by construction: it never calls a provider or a model.</summary>
    public bool Deterministic { get; init; } = true;

    public required IncidentId IncidentId { get; init; }

    public required long StreamVersion { get; init; }

    public required int EventCount { get; init; }

    /// <summary>SHA-256 of the canonical state rebuilt from the stream.</summary>
    public required string StateHash { get; init; }

    /// <summary>Whether a second rebuild of the same stream produced the same state hash.</summary>
    public required bool StateStable { get; init; }

    /// <summary>Whether the projection rebuilt in memory equals the stored one.</summary>
    public required bool ProjectionMatches { get; init; }

    public IReadOnlyList<string> ProjectionDifferences { get; init; } = [];

    public IReadOnlyList<DecisionReplay> Decisions { get; init; } = [];

    public required DateTimeOffset ReplayedAt { get; init; }
}

/// <summary>Asks AgentWorker to replay one agent execution at level 2 or 3.</summary>
public sealed record InvestigationReplayRequest
{
    public ReplayLevel Level { get; init; } = ReplayLevel.Investigation;

    /// <summary>Level 3 only: the model profile to reevaluate with; null keeps the original role's route.</summary>
    public string? ModelProfile { get; init; }

    /// <summary>Level 3 only: the prompt version to reevaluate with; null keeps the original.</summary>
    public string? PromptVersion { get; init; }
}

/// <summary>A tool call whose replayed result differed from the recorded one.</summary>
public sealed record ToolCallMismatch(string ToolCallId, string ToolName, string Reason);

/// <summary>
/// The result of replaying an agent execution. Inputs were served only from what the execution
/// captured; a call outside them failed explicitly rather than reaching a live provider.
/// </summary>
public sealed record InvestigationReplayResult
{
    public required Guid ReplayId { get; init; }

    public required AgentExecutionId AgentExecutionId { get; init; }

    public required ReplayLevel Level { get; init; }

    /// <summary>True only when every step was deterministic: captured inputs and a deterministic model, or no model call at all.</summary>
    public required bool Deterministic { get; init; }

    /// <summary>Whether re-running every recorded tool call over the captured provider responses reproduced its recorded result.</summary>
    public required bool InputsReproduced { get; init; }

    public required int ToolCallsReplayed { get; init; }

    public IReadOnlyList<ToolCallMismatch> ToolCallMismatches { get; init; } = [];

    /// <summary>Provider queries the replay needed that the execution never captured.</summary>
    public int UncapturedQueries { get; init; }

    /// <summary>Null when the investigator was not rerun (level 2 with a nondeterministic model).</summary>
    public bool? FindingsMatch { get; init; }

    public IReadOnlyList<string> Differences { get; init; } = [];

    /// <summary>Cited observations the replayed findings did not receive from the frozen inputs: fabricated evidence.</summary>
    public IReadOnlyList<ObservationId> UnsupportedCitations { get; init; } = [];

    public ModelMetadata? Model { get; init; }

    public InvestigationCompleted? Replayed { get; init; }

    public required string Explanation { get; init; }

    public required DateTimeOffset ReplayedAt { get; init; }
}

/// <summary>Where a postmortem fact came from: an event of the stream, evidence, an observation, an investigation or an execution.</summary>
public sealed record SourceRef(string Kind, string Ref);

/// <summary>One statement of a postmortem and the history it is derived from.</summary>
public sealed record PostmortemFact(string Text, IReadOnlyList<SourceRef> Sources, DateTimeOffset? At = null);

/// <summary>The human-written part of a postmortem, stored and versioned by Api.</summary>
public sealed record PostmortemNarrative(string Markdown, int Version, string EditedBy, DateTimeOffset EditedAt);

/// <summary>
/// A postmortem generated from authoritative structured history (Phase 07): every fact links to the
/// events and evidence it came from, and the narrative is the only part a human writes.
/// </summary>
public sealed record Postmortem
{
    public required IncidentId IncidentId { get; init; }

    public required string Title { get; init; }

    public required IncidentStatus Status { get; init; }

    public required long StreamVersion { get; init; }

    public required DateTimeOffset GeneratedAt { get; init; }

    public required PostmortemFact Summary { get; init; }

    public IReadOnlyList<PostmortemFact> Timeline { get; init; } = [];

    public IReadOnlyList<PostmortemFact> Evidence { get; init; } = [];

    public IReadOnlyList<PostmortemFact> Decision { get; init; } = [];

    public IReadOnlyList<PostmortemFact> Actions { get; init; } = [];

    public IReadOnlyList<PostmortemFact> Recovery { get; init; } = [];

    /// <summary>What the investigation could not determine, and dissent that was kept.</summary>
    public IReadOnlyList<PostmortemFact> OpenQuestions { get; init; } = [];

    public required VersionManifest Versions { get; init; }

    public PostmortemNarrative? Narrative { get; init; }
}
