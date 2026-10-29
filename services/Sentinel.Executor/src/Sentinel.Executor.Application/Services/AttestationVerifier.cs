using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application.Abstractions.Security;

namespace Sentinel.Executor.Application.Services;

/// <summary>
/// Verifies Api's approval attestation (ADR-006): an ES256 compact JWS whose signature checks out
/// against a published key, whose audience is Executor, which has not expired, and whose claims bind
/// it to exactly this tenant, environment, incident, plan ID, version and content hash.
/// Single use is enforced separately, when the attestation is consumed.
/// </summary>
public sealed class AttestationVerifier(IApprovalKeyResolver keys, TimeProvider time)
{
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public async Task<Result<ApprovalAttestationClaims>> VerifyAsync(string attestation, ExecuteRemediation command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        string[] parts = (attestation ?? string.Empty).Split('.');
        if (parts.Length != 3)
        {
            return Fail("The attestation is not a compact JWS.");
        }

        ApprovalAttestationHeader? header;
        ApprovalAttestationClaims? claims;
        byte[] signature;
        try
        {
            header = JsonSerializer.Deserialize<ApprovalAttestationHeader>(Base64Url.DecodeFromChars(parts[0]), SentinelJson.Options);
            claims = JsonSerializer.Deserialize<ApprovalAttestationClaims>(Base64Url.DecodeFromChars(parts[1]), SentinelJson.Options);
            signature = Base64Url.DecodeFromChars(parts[2]);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return Fail($"The attestation cannot be decoded: {exception.Message}");
        }

        if (header is not { Algorithm: ApprovalAttestation.Algorithm, Type: ApprovalAttestation.TokenType } || claims is null)
        {
            return Fail("The attestation must be an ES256 Sentinel approval.");
        }

        ECDsa? key = await keys.ResolveAsync(header.KeyId, cancellationToken);
        if (key is null)
        {
            return Fail($"Key {header.KeyId} is not a published approval key.");
        }

        byte[] signed = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        if (!key.VerifyData(signed, signature, HashAlgorithmName.SHA256))
        {
            return Fail("The attestation signature is invalid.");
        }

        DateTimeOffset now = time.GetUtcNow();
        string? problem = claims switch
        {
            { Issuer: not ApprovalAttestation.Issuer } => $"Issuer {claims.Issuer} is not Sentinel.Api.",
            { Audience: not ApprovalAttestation.Audience } => $"Audience {claims.Audience} is not Sentinel.Executor.",
            { ApprovalScope: not ApprovalAttestation.ExecutePlanScope } => $"Scope {claims.ApprovalScope} does not permit execution.",
            _ when claims.ExpiresAt <= now - ClockSkew => $"The attestation expired at {claims.ExpiresAt:O}.",
            _ when claims.IssuedAt > now + ClockSkew => "The attestation was issued in the future.",
            _ when claims.TenantId != command.TenantId || claims.EnvironmentId != command.EnvironmentId => "The attestation is for another tenant or environment.",
            _ when claims.IncidentId != command.IncidentId => "The attestation is for another incident.",
            _ when claims.PlanId != command.Plan.PlanId || claims.PlanVersion != command.Plan.PlanVersion => "The attestation approves another plan version.",
            _ when claims.PlanHash != command.Plan.PlanHash => "The attestation approves different plan content.",
            _ => null
        };

        return problem is null ? Result.Success(claims) : Fail(problem);
    }

    private static Result<ApprovalAttestationClaims> Fail(string reason) =>
        Result.Failure<ApprovalAttestationClaims>(Error.Problem("Attestation.Invalid", reason));
}
