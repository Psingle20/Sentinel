using System.Globalization;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Domain.Correlation.Decisions;

public sealed record SuppressSignal(IReadOnlyList<string> Reasons) : CorrelationDecision(Reasons);
