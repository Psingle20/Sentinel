using System.Globalization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Domain.Correlation.Decisions;

/// <summary>The service already has an open incident; the sample is absorbed.</summary>
public sealed record AbsorbIntoIncident(IncidentId IncidentId, IReadOnlyList<string> Reasons) : CorrelationDecision(Reasons);
