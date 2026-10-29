using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Contracts.Incidents;

public sealed record HypothesisAssessmentView(HypothesisId HypothesisId, double Score, int IndependentSupport, int IndependentContradiction, bool Eliminated, string Reason);
