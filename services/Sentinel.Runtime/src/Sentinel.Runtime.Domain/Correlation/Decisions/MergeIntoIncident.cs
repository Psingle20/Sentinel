using System.Globalization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Domain.Correlation.Decisions;

/// <summary>The signal is another symptom of an open incident on a related service (blast radius).</summary>
public sealed record MergeIntoIncident(IncidentId IncidentId, double Score, IReadOnlyList<string> Reasons) : CorrelationDecision(Reasons);
