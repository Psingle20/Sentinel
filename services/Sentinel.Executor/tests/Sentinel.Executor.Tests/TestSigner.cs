using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Application.Abstractions.Providers;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Application.Abstractions.Security;

namespace Sentinel.Executor.Tests;

/// <summary>Plays Api's signer: an in-memory key whose public half the resolver publishes.</summary>
internal sealed class TestSigner : IApprovalKeyResolver, IDisposable
{
    public const string KeyId = "test-key-1";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public Task<ECDsa?> ResolveAsync(string keyId, CancellationToken cancellationToken) =>
        Task.FromResult(keyId == KeyId ? _key : null);

    public string Sign(ApprovalAttestationClaims claims, string keyId = KeyId, ECDsa? key = null)
    {
        string header = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new ApprovalAttestationHeader(ApprovalAttestation.Algorithm, keyId, ApprovalAttestation.TokenType), SentinelJson.Options));
        string payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(claims, SentinelJson.Options));
        byte[] signature = (key ?? _key).SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256);
        return $"{header}.{payload}.{Base64Url.EncodeToString(signature)}";
    }

    public void Dispose() => _key.Dispose();
}
