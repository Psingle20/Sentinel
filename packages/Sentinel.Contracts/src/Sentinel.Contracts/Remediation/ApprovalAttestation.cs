using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>Fixed values of the approval attestation: a compact ES256 JWS signed by Api (ADR-006).</summary>
public static class ApprovalAttestation
{
    public const string Algorithm = "ES256";
    public const string TokenType = "sentinel-approval+jws";
    public const string Issuer = "sentinel-api";
    public const string Audience = "sentinel-executor";
    public const string ExecutePlanScope = "execute-plan";

    /// <summary>Where Api publishes its verification keys.</summary>
    public const string KeysPath = "/.well-known/sentinel/approval-keys";
}

/// <summary>The JWS protected header.</summary>
public sealed record ApprovalAttestationHeader(
    [property: JsonPropertyName("alg")] string Algorithm,
    [property: JsonPropertyName("kid")] string KeyId,
    [property: JsonPropertyName("typ")] string Type);

/// <summary>The claims Api signs when an approver approves one immutable plan version (ADR-006).</summary>
public sealed record ApprovalAttestationClaims
{
    public required AttestationId AttestationId { get; init; }

    public required TenantId TenantId { get; init; }

    public required EnvironmentId EnvironmentId { get; init; }

    public required IncidentId IncidentId { get; init; }

    public required PlanId PlanId { get; init; }

    public required int PlanVersion { get; init; }

    public required string PlanHash { get; init; }

    public required string ApproverId { get; init; }

    public required string ApprovalScope { get; init; }

    /// <summary>The policy version of the plan-time evaluation the approver saw.</summary>
    public required string PolicyVersion { get; init; }

    public required DateTimeOffset IssuedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public required string Issuer { get; init; }

    public required string Audience { get; init; }
}

/// <summary>One verification key in JWK form; Executor resolves it by <see cref="KeyId"/>.</summary>
public sealed record ApprovalVerificationKey(
    [property: JsonPropertyName("kid")] string KeyId,
    [property: JsonPropertyName("kty")] string KeyType,
    [property: JsonPropertyName("crv")] string Curve,
    [property: JsonPropertyName("x")] string X,
    [property: JsonPropertyName("y")] string Y,
    [property: JsonPropertyName("alg")] string Algorithm,
    [property: JsonPropertyName("use")] string Use,
    [property: JsonPropertyName("nbf")] long NotBefore,
    [property: JsonPropertyName("exp")] long? Expires);

public sealed record ApprovalKeySet([property: JsonPropertyName("keys")] IReadOnlyList<ApprovalVerificationKey> Keys);

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
