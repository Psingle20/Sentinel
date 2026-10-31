using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Investigations;

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

    /// <summary>Text in the investigation's untrusted tool results that read as an instruction to an AI agent; reported, never obeyed.</summary>
    public IReadOnlyList<SuspectedInjection>? SuspectedInjections { get; init; }

    /// <summary>For the escalation investigator (Phase E05): the observation it proposed, which AgentWorker ran and measured.</summary>
    public DiscriminatingQuery? Query { get; init; }

    /// <summary>The version of the tool catalog the investigator could call, for the run's version manifest (Phase 07).</summary>
    public string? ToolCatalogVersion { get; init; }
}
