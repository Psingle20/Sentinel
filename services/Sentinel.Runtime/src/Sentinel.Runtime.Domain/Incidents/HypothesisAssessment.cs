using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

public sealed record HypothesisAssessment(
    HypothesisId HypothesisId,
    double Score,
    int IndependentSupport,
    int IndependentContradiction,
    bool Falsified,
    string Reason);
