using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Application.Incidents.Commands;

/// <summary>The escalation investigation a decision may request instead of abstaining for ambiguity (Phase E05).</summary>
public sealed record EscalationSpec(InvestigationId InvestigationId, string Objective, DateTimeOffset WindowFrom, DateTimeOffset WindowTo, double MinimumCompleteness);
