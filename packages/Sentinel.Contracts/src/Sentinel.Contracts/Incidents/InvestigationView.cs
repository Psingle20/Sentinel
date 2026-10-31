using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// Which investigator ran, with which model, and how it ended; for the escalation investigator, the one query it
/// proposed (Phase E05).
/// </summary>
public sealed record InvestigationView(
    InvestigationId InvestigationId,
    DateTimeOffset RequestedAt,
    AgentExecutionId? AgentExecutionId,
    InvestigationOutcome? Outcome,
    ModelMetadata? Model,
    string? AbstentionReason,
    DateTimeOffset? CompletedAt,
    string? Role = null,
    bool TimedOut = false,
    string? Query = null);
