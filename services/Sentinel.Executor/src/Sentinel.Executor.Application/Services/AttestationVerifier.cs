using System.Security.Cryptography;
using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application.Abstractions.Security;

namespace Sentinel.Executor.Application.Services;

/// <summary>
/// Verifies what the Gateway signed (ADR-006): an ES256 compact JWS whose signature checks out
/// against a published key, whose audience is Executor and which has not expired. An approval
/// attestation must bind to exactly this tenant, environment, incident, plan ID, version and content
/// hash; an operator attestation to exactly this scope, resource and submitted content. Single use is
/// enforced separately, when the attestation is consumed.
/// </summary>
public sealed class AttestationVerifier(IApprovalKeyResolver keys, TimeProvider time)
{
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public async Task<Result<ApprovalAttestationClaims>> VerifyAsync(string attestation, ExecuteRemediation command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<ApprovalAttestationClaims> signed = await VerifySignedAsync<ApprovalAttestationClaims>(attestation, ApprovalAttestation.TokenType, "approval", cancellationToken);
        if (signed.IsFailure)
        {
            return signed;
        }

        ApprovalAttestationClaims claims = signed.Value;
        DateTimeOffset now = time.GetUtcNow();
        string? problem = claims switch
        {
            { Issuer: not ApprovalAttestation.Issuer } => $"Issuer {claims.Issuer} is not the Gateway.",
            { Audience: not ApprovalAttestation.Audience } => $"Audience {claims.Audience} is not Executor.",
            { ApprovalScope: not ApprovalAttestation.ExecutePlanScope } => $"Scope {claims.ApprovalScope} does not permit execution.",
            _ when claims.ExpiresAt <= now - ClockSkew => $"The attestation expired at {claims.ExpiresAt:O}.",
            _ when claims.IssuedAt > now + ClockSkew => "The attestation was issued in the future.",
            _ when claims.TenantId != command.TenantId || claims.EnvironmentId != command.EnvironmentId => "The attestation is for another tenant or environment.",
            _ when claims.IncidentId != command.IncidentId => "The attestation is for another incident.",
            _ when claims.PlanId != command.Plan.PlanId || claims.PlanVersion != command.Plan.PlanVersion => "The attestation approves another plan version.",
            _ when claims.PlanHash != command.Plan.PlanHash => "The attestation approves different plan content.",
            _ => null
        };

        return problem is null ? Result.Success(claims) : Fail<ApprovalAttestationClaims>(problem);
    }

    /// <summary>
    /// An operator's attestation for one control-plane action: signed by the Gateway for Executor, for
    /// <paramref name="scope"/> on <paramref name="resource"/>, over exactly <paramref name="contentHash"/>.
    /// </summary>
    public async Task<Result<OperatorAttestationClaims>> VerifyOperatorAsync(string? attestation, string scope, string resource, string contentHash, CancellationToken cancellationToken)
    {
        Result<OperatorAttestationClaims> signed = await VerifySignedAsync<OperatorAttestationClaims>(attestation, ApprovalAttestation.OperatorTokenType, "operator", cancellationToken);
        if (signed.IsFailure)
        {
            return signed;
        }

        OperatorAttestationClaims claims = signed.Value;
        DateTimeOffset now = time.GetUtcNow();
        string? problem = claims switch
        {
            { Issuer: not ApprovalAttestation.Issuer } => $"Issuer {claims.Issuer} is not the Gateway.",
            { Audience: not ApprovalAttestation.Audience } => $"Audience {claims.Audience} is not Executor.",
            _ when claims.Scope != scope => $"Scope {claims.Scope} does not permit {scope}.",
            _ when claims.Resource != resource => $"The attestation is for {claims.Resource}, not {resource}.",
            _ when claims.ExpiresAt <= now - ClockSkew => $"The attestation expired at {claims.ExpiresAt:O}.",
            _ when claims.IssuedAt > now + ClockSkew => "The attestation was issued in the future.",
            _ when !string.Equals(claims.ContentHash, contentHash, StringComparison.Ordinal) => "The attestation covers different content than was submitted.",
            _ when string.IsNullOrWhiteSpace(claims.ActorId) => "The attestation names no actor.",
            _ => null
        };

        return problem is null ? Result.Success(claims) : Fail<OperatorAttestationClaims>(problem);
    }

    private async Task<Result<TClaims>> VerifySignedAsync<TClaims>(string? attestation, string tokenType, string kind, CancellationToken cancellationToken)
        where TClaims : class
    {
        if (!AttestationToken.TryDecode<TClaims>(attestation, out DecodedAttestation<TClaims> decoded, out string problem))
        {
            return Fail<TClaims>(problem);
        }

        if (decoded.Header is not { Algorithm: ApprovalAttestation.Algorithm } || decoded.Header.Type != tokenType)
        {
            return Fail<TClaims>($"The attestation must be an ES256 Sentinel {kind} attestation.");
        }

        ECDsa? key = await keys.ResolveAsync(decoded.Header.KeyId, cancellationToken);
        if (key is null)
        {
            return Fail<TClaims>($"Key {decoded.Header.KeyId} is not a published approval key.");
        }

        return AttestationToken.VerifySignature(decoded, key)
            ? Result.Success(decoded.Claims)
            : Fail<TClaims>("The attestation signature is invalid.");
    }

    private static Result<TClaims> Fail<TClaims>(string reason) =>
        Result.Failure<TClaims>(Error.Problem("Attestation.Invalid", reason));
}
