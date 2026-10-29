using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Investigations;

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
