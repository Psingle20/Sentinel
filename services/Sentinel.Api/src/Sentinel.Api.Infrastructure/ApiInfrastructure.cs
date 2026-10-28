using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Api.Application.Approvals;
using Sentinel.Api.Domain.Approvals;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Api.Infrastructure;

public static class ApiInfrastructure
{
    public static IServiceCollection AddApiInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ApprovalOptions>().Bind(configuration.GetSection(ApprovalOptions.Section));
        services.AddOptions<SigningOptions>().Bind(configuration.GetSection(SigningOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMessagingStoreInitializer, ApiSchema>();
        services.AddSingleton<IApprovalSigner, LocalApprovalSigner>();
        services.AddScoped<IApprovalStore, PostgresApprovalStore>();
        services.AddScoped<ApprovalService>();

        Uri runtime = new(configuration["Api:Services:Runtime"] ?? throw new InvalidOperationException("Api:Services:Runtime is not configured."));
        services.AddHttpClient<IRuntimeIncidents, RuntimeIncidentsClient>(client => client.BaseAddress = runtime);
        return services;
    }
}

internal sealed class RuntimeIncidentsClient(HttpClient http) : IRuntimeIncidents
{
    public async Task<IReadOnlyList<IncidentSummary>> ListAsync(int limit, CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<List<IncidentSummary>>($"/v1/incidents?limit={limit}", SentinelJson.Options, cancellationToken) ?? [];

    public Task<IncidentDetail?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        GetOrNullAsync<IncidentDetail>($"/v1/incidents/{incidentId}", cancellationToken);

    public Task<IncidentRunRecord?> RunRecordAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        GetOrNullAsync<IncidentRunRecord>($"/v1/incidents/{incidentId}/run-record", cancellationToken);

    private async Task<TValue?> GetOrNullAsync<TValue>(string uri, CancellationToken cancellationToken)
        where TValue : class
    {
        using HttpResponseMessage response = await http.GetAsync(new Uri(uri, UriKind.Relative), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TValue>(SentinelJson.Options, cancellationToken);
    }
}

/// <summary>Configured as <c>Api:Signing</c>.</summary>
public sealed class SigningOptions
{
    public const string Section = "Api:Signing";

    /// <summary>A new signing key is generated after this long; the previous one stays published until its attestations expire.</summary>
    public TimeSpan RotateAfter { get; set; } = TimeSpan.FromHours(12);
}

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

internal sealed class ApiSchema(NpgsqlDataSource dataSource) : IMessagingStoreInitializer
{
    private const string Sql = """
        select pg_advisory_xact_lock(hashtext('sentinel.api.schema'));

        create schema if not exists api;

        create table if not exists api.approvals (
            approval_id uuid primary key,
            incident_id uuid not null,
            plan_id uuid not null,
            plan_version integer not null,
            decision text not null,
            record jsonb not null,
            decided_at timestamptz not null,
            unique (incident_id, plan_id, plan_version)
        );

        create table if not exists api.approval_keys (
            key_id text primary key,
            x text not null,
            y text not null,
            not_before timestamptz not null,
            expires timestamptz
        );
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

internal sealed class PostgresApprovalStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : IApprovalStore
{
    public async Task<ApprovalRecord?> FindAsync(IncidentId incidentId, PlanId planId, int planVersion, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "select record from api.approvals where incident_id = @incident_id and plan_id = @plan_id and plan_version = @plan_version");
        command.Parameters.AddWithValue("incident_id", incidentId.Value);
        command.Parameters.AddWithValue("plan_id", planId.Value);
        command.Parameters.AddWithValue("plan_version", planVersion);
        return await command.ExecuteScalarAsync(cancellationToken) is string json
            ? JsonSerializer.Deserialize<ApprovalRecord>(json, SentinelJson.Options)
            : null;
    }

    public async Task SaveAsync(ApprovalRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into api.approvals (approval_id, incident_id, plan_id, plan_version, decision, record, decided_at)
            values (@approval_id, @incident_id, @plan_id, @plan_version, @decision, @record, @decided_at)
            on conflict (approval_id) do update set decision = excluded.decision, record = excluded.record
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("approval_id", record.ApprovalId);
        command.Parameters.AddWithValue("incident_id", record.IncidentId.Value);
        command.Parameters.AddWithValue("plan_id", record.PlanId.Value);
        command.Parameters.AddWithValue("plan_version", record.PlanVersion);
        command.Parameters.AddWithValue("decision", record.Decision.ToString());
        command.Parameters.Add(new NpgsqlParameter("record", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(record, SentinelJson.Options) });
        command.Parameters.AddWithValue("decided_at", record.DecidedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
