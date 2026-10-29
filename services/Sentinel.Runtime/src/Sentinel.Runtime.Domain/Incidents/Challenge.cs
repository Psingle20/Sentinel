using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>An adversarial challenge to one hypothesis: the falsifier asked and what the evidence said.</summary>
public sealed record Challenge(HypothesisId HypothesisId, string Falsifier, ChallengeVerdict Verdict, IReadOnlyList<EvidenceId> EvidenceIds, string Detail);
