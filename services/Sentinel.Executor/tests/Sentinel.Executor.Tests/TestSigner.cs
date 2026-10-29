using System.Security.Cryptography;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application.Abstractions.Security;

namespace Sentinel.Executor.Tests;

/// <summary>Plays the Gateway's signer: an in-memory key whose public half the resolver publishes.</summary>
internal sealed class TestSigner : IApprovalKeyResolver, IDisposable
{
    public const string KeyId = "test-key-1";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public Task<ECDsa?> ResolveAsync(string keyId, CancellationToken cancellationToken) =>
        Task.FromResult(keyId == KeyId ? _key : null);

    public string Sign(ApprovalAttestationClaims claims, string keyId = KeyId, ECDsa? key = null) =>
        AttestationToken.Encode(new ApprovalAttestationHeader(ApprovalAttestation.Algorithm, keyId, ApprovalAttestation.TokenType), claims, key ?? _key);

    public string SignOperator(OperatorAttestationClaims claims, string keyId = KeyId, ECDsa? key = null, string tokenType = ApprovalAttestation.OperatorTokenType) =>
        AttestationToken.Encode(new ApprovalAttestationHeader(ApprovalAttestation.Algorithm, keyId, tokenType), claims, key ?? _key);

    public void Dispose() => _key.Dispose();
}
