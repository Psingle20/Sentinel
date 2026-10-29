using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Sentinel.Gateway.Application.Abstractions.Security;
using Sentinel.Gateway.Application.Options;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Domain.Approvals;

namespace Sentinel.Gateway.Infrastructure.Security;

/// <summary>
/// The local development implementation of <see cref="IApprovalSigner"/> (ADR-006): an ES256 key
/// pair generated in memory at startup and never written anywhere. Only public keys are stored,
/// so attestations signed before a restart or rotation stay verifiable until they expire. In
/// production this interface is backed by a managed, non-exportable signing key.
/// </summary>
internal sealed class LocalApprovalSigner(
    NpgsqlDataSource dataSource,
    TimeProvider time,
    IOptions<SigningOptions> signing,
    IOptions<ApprovalOptions> approvals,
    ILogger<LocalApprovalSigner> logger) : IApprovalSigner, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ECDsa? _key;
    private string? _keyId;
    private DateTimeOffset _createdAt;

    public async Task<SignedAttestation> SignAsync(ApprovalAttestationClaims claims, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claims);

        (ECDsa key, string keyId) = await CurrentKeyAsync(cancellationToken);
        string header = Encode(JsonSerializer.SerializeToUtf8Bytes(new ApprovalAttestationHeader(ApprovalAttestation.Algorithm, keyId, ApprovalAttestation.TokenType), SentinelJson.Options));
        string payload = Encode(JsonSerializer.SerializeToUtf8Bytes(claims, SentinelJson.Options));
        byte[] signature = key.SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256);
        return new SignedAttestation($"{header}.{payload}.{Encode(signature)}", keyId);
    }

    public async Task<ApprovalKeySet> PublishedKeysAsync(CancellationToken cancellationToken)
    {
        await CurrentKeyAsync(cancellationToken);
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "select key_id, x, y, not_before, expires from api.approval_keys where expires is null or expires > @now order by not_before");
        command.Parameters.AddWithValue("now", time.GetUtcNow());
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var keys = new List<ApprovalVerificationKey>();
        while (await reader.ReadAsync(cancellationToken))
        {
            DateTimeOffset? expires = await reader.IsDBNullAsync(4, cancellationToken) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(4, cancellationToken);
            keys.Add(new ApprovalVerificationKey(
                reader.GetString(0), "EC", "P-256", reader.GetString(1), reader.GetString(2), ApprovalAttestation.Algorithm, "sig",
                (await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken)).ToUnixTimeSeconds(),
                expires?.ToUnixTimeSeconds()));
        }

        return new ApprovalKeySet(keys);
    }

    public void Dispose()
    {
        _key?.Dispose();
        _gate.Dispose();
    }

    private async Task<(ECDsa Key, string KeyId)> CurrentKeyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset now = time.GetUtcNow();
            if (_key is null || now - _createdAt >= signing.Value.RotateAfter)
            {
                await RotateAsync(now, cancellationToken);
            }

            return (_key!, _keyId!);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RotateAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
        string keyId = $"api-{now:yyyyMMddHHmmss}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}";

        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (_keyId is not null)
        {
            // The retiring key stays published for as long as an attestation it signed can be valid.
            await using var retire = new NpgsqlCommand("update api.approval_keys set expires = @expires where key_id = @key_id", connection, transaction);
            retire.Parameters.AddWithValue("expires", now + approvals.Value.AttestationLifetime + TimeSpan.FromMinutes(5));
            retire.Parameters.AddWithValue("key_id", _keyId);
            await retire.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insert = new NpgsqlCommand("insert into api.approval_keys (key_id, x, y, not_before) values (@key_id, @x, @y, @not_before)", connection, transaction);
        insert.Parameters.AddWithValue("key_id", keyId);
        insert.Parameters.AddWithValue("x", Encode(parameters.Q.X!));
        insert.Parameters.AddWithValue("y", Encode(parameters.Q.Y!));
        insert.Parameters.AddWithValue("not_before", now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _key?.Dispose();
        _key = key;
        _keyId = keyId;
        _createdAt = now;
        logger.LogInformation("Approval signing key {KeyId} is active", keyId);
    }

    private static string Encode(byte[] bytes) => Base64Url.EncodeToString(bytes);
}
