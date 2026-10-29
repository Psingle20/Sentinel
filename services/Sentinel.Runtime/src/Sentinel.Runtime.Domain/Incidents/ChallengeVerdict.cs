using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

public enum ChallengeVerdict
{
    Survived,
    Falsified,
    Inconclusive
}
