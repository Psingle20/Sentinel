using System.Globalization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Domain.Correlation.Decisions;

/// <summary>A new incident, with the changes worth investigating and why each was linked.</summary>
public sealed record OpenIncident(IReadOnlyList<LinkedChange> Changes, IReadOnlyList<string> Reasons) : CorrelationDecision(Reasons);
