using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>
/// The content hash that binds approval to one plan version: SHA-256 over the plan's canonical
/// JSON with <see cref="RemediationPlan.PlanHash"/> blanked. Runtime computes it, Api and
/// Executor recompute it.
/// </summary>
public static class RemediationPlanHash
{
    public const string Prefix = "sha256:";

    public static string Compute(RemediationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(plan with { PlanHash = string.Empty }, SentinelJson.Options);
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(canonical));
    }

    public static bool Matches(RemediationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(plan)), Encoding.ASCII.GetBytes(plan.PlanHash));
    }
}
