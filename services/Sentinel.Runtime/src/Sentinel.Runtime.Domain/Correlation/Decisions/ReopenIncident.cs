using System.Globalization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Domain.Correlation.Decisions;

public sealed record ReopenIncident(IncidentId IncidentId, IReadOnlyList<string> Reasons) : CorrelationDecision(Reasons);
