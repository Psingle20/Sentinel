using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Api.Application.Analytics;
using Sentinel.Api.Application.Approvals;
using Sentinel.Api.Application.ControlPlane;
using Sentinel.Api.Application.Idempotency;
using Sentinel.Api.Application.Live;
using Sentinel.Api.Application.Postmortems;
using Sentinel.Api.Domain.Approvals;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Api.Infrastructure;

public static class ApiInfrastructure
{
    /// <summary>The options name for the deployment configuration without the control-plane document.</summary>
    public const string DeploymentOptions = "deployment";

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
        services.AddHttpClient<IRuntimeHistory, RuntimeHistoryClient>(client => client.BaseAddress = runtime);

        Uri agentWorker = new(configuration["Api:Services:AgentWorker"] ?? throw new InvalidOperationException("Api:Services:AgentWorker is not configured."));
        services.AddHttpClient<IAgentExecutions, AgentExecutionsClient>(client => client.BaseAddress = agentWorker);
        services.AddScoped<INarrativeStore, PostgresNarrativeStore>();
        services.AddScoped<PostmortemService>();

        // The control plane (Phase 08): Executor owns policy and controls, Ingestion its sources; Api authorizes and audits.
        Uri executor = new(configuration["Api:Services:Executor"] ?? throw new InvalidOperationException("Api:Services:Executor is not configured."));
        Uri ingestion = new(configuration["Api:Services:Ingestion"] ?? throw new InvalidOperationException("Api:Services:Ingestion is not configured."));
        services.AddHttpClient<IExecutorControlPlane, ExecutorControlPlaneClient>(client => client.BaseAddress = executor);
        services.AddHttpClient<ISourceHealth, SourceHealthClient>(client => client.BaseAddress = ingestion);
        services.AddScoped<IAuditLog, PostgresAuditLog>();
        services.AddScoped<ControlPlaneService>();
        services.AddLiveUpdates(configuration);

        // Phase 08 configuration areas: Runtime, AgentWorker and Ingestion own theirs; Api owns tenants and notifications.
        services.AddHttpClient(ConfigurationAreas.Runtime, client => client.BaseAddress = runtime);
        services.AddHttpClient(ConfigurationAreas.Models, client => client.BaseAddress = agentWorker);
        services.AddHttpClient(ConfigurationAreas.Sources, client => client.BaseAddress = ingestion);
        services.AddOptions<TenantCatalogOptions>().Bind(configuration.GetSection(TenantCatalogOptions.Section));
        services.AddOptions<TenantCatalogOptions>(DeploymentOptions).Bind(configuration.GetSection(TenantCatalogOptions.Section));
        services.AddOptions<NotificationOptions>(DeploymentOptions).Bind(configuration.GetSection(NotificationOptions.Section));
        services.AddSingleton<IVersionedDocumentStore<ApiConfigurationDocument>>(provider =>
            new PostgresVersionedDocumentStore<ApiConfigurationDocument>(provider.GetRequiredService<NpgsqlDataSource>(), ApiSchema.ConfigurationTable, SentinelJson.Options));
        services.AddSingleton<VersionedDocumentPublisher<ApiConfigurationDocument>>();
        services.AddSingleton(provider =>
        {
            NotificationOptions deployed = provider.GetRequiredService<IOptionsFactory<NotificationOptions>>().Create(DeploymentOptions);
            return new ApiConfigurationService(
                provider.GetRequiredService<VersionedDocumentPublisher<ApiConfigurationDocument>>(),
                () => ApiConfigurationMapping.From(
                    provider.GetRequiredService<IOptionsFactory<TenantCatalogOptions>>().Create(DeploymentOptions),
                    provider.GetRequiredService<IOptionsFactory<NotificationOptions>>().Create(DeploymentOptions)),
                [.. deployed.AllowedWebhookHosts],
                key => !string.IsNullOrEmpty(configuration[key]));
        });
        services.AddManagedDocument<TenantCatalogOptions, ApiConfigurationDocument>(ApiConfigurationMapping.Apply);
        services.AddManagedDocument<NotificationOptions, ApiConfigurationDocument>(ApiConfigurationMapping.Apply);
        services.AddSingleton<IConfigurationOwners, ConfigurationOwnersClient>();
        services.AddSingleton<IIdempotencyStore, PostgresIdempotencyStore>();
        services.AddSingleton<IAnalyticsStore, PostgresAnalyticsStore>();
        services.AddSingleton<ProductAnalyticsService>();
        return services;
    }
}

/// <summary>
/// Each area's document at the service that owns it, over HTTP; Api's own area in process. Bodies
/// pass through unchanged, so every owner answers with its document state, history or problem.
/// </summary>
internal sealed class ConfigurationOwnersClient(IHttpClientFactory clients, ApiConfigurationService api, ManagedDocument<ApiConfigurationDocument> current) : IConfigurationOwners
{
    public async Task<ControlPlaneResponse> CurrentAsync(string area, CancellationToken cancellationToken) =>
        area == ConfigurationAreas.Api
            ? Ok(await api.CurrentAsync(cancellationToken))
            : await ControlPlaneHttp.SendAsync(Client(area), HttpMethod.Get, "/v1/configuration", null, cancellationToken);

    public async Task<ControlPlaneResponse> HistoryAsync(string area, CancellationToken cancellationToken) =>
        area == ConfigurationAreas.Api
            ? Ok(await api.HistoryAsync(cancellationToken))
            : await ControlPlaneHttp.SendAsync(Client(area), HttpMethod.Get, "/v1/configuration/history", null, cancellationToken);

    public async Task<ControlPlaneResponse> PublishAsync(string area, JsonElement document, string expectedVersion, string author, CancellationToken cancellationToken)
    {
        if (area != ConfigurationAreas.Api)
        {
            return await ControlPlaneHttp.SendAsync(Client(area), HttpMethod.Put, "/admin/configuration", new { document, expectedVersion, author }, cancellationToken);
        }

        ApiConfigurationDocument? parsed;
        try
        {
            parsed = document.Deserialize<ApiConfigurationDocument>(SentinelJson.Options);
        }
        catch (JsonException exception)
        {
            return new ControlPlaneResponse(400, null, $"The document does not match Api's configuration: {exception.Message}");
        }

        if (parsed is null)
        {
            return new ControlPlaneResponse(400, null, "The document is empty.");
        }

        CleanArchitecture.BuildingBlocks.Result<PublishedDocument<ApiConfigurationDocument>> published = await api.PublishAsync(parsed, expectedVersion, author, cancellationToken);
        if (published.IsFailure)
        {
            return new ControlPlaneResponse(published.Error.Type == CleanArchitecture.BuildingBlocks.ErrorType.Conflict ? 409 : 400, null, published.Error.Description);
        }

        current.Set(published.Value);
        return Ok(published.Value.Version);
    }

    private static ControlPlaneResponse Ok<T>(T value) => new(200, JsonSerializer.SerializeToElement(value, SentinelJson.Options), null);

    private HttpClient Client(string area) => clients.CreateClient(area);
}

internal static class ControlPlaneHttp
{
    public static async Task<ControlPlaneResponse> SendAsync(HttpClient http, HttpMethod method, string uri, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(uri, UriKind.Relative))
        {
            Content = body is null ? null : JsonContent.Create(body, options: SentinelJson.Options)
        };
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new ControlPlaneResponse((int)response.StatusCode, null, Describe(text) ?? response.ReasonPhrase);
        }

        return new ControlPlaneResponse((int)response.StatusCode, text.Length > 0 ? JsonDocument.Parse(text).RootElement.Clone() : null, null);
    }
    // The owning service's problem details carry its reasons in detail (or title); pass those on, not the raw JSON.
    private static string? Describe(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        try
        {
            using var problem = JsonDocument.Parse(text);
            JsonElement root = problem.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (string property in (string[])["detail", "title"])
                {
                    if (root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } described)
                    {
                        return described;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON: the text is the description.
        }

        return text;
    }
}

internal sealed class ExecutorControlPlaneClient(HttpClient http) : IExecutorControlPlane
{
    public Task<ControlPlaneResponse> PolicyAsync(CancellationToken cancellationToken) => ControlPlaneHttp.SendAsync(http, HttpMethod.Get, "/v1/policy", null, cancellationToken);

    public Task<ControlPlaneResponse> PolicyHistoryAsync(CancellationToken cancellationToken) => ControlPlaneHttp.SendAsync(http, HttpMethod.Get, "/v1/policy/history", null, cancellationToken);

    public Task<ControlPlaneResponse> PublishPolicyAsync(JsonElement document, string expectedVersion, string author, CancellationToken cancellationToken) =>
        ControlPlaneHttp.SendAsync(http, HttpMethod.Put, "/admin/policy", new { document, expectedVersion, author }, cancellationToken);

    public Task<ControlPlaneResponse> ControlsAsync(CancellationToken cancellationToken) => ControlPlaneHttp.SendAsync(http, HttpMethod.Get, "/admin/controls", null, cancellationToken);

    public Task<ControlPlaneResponse> SetEmergencyStopAsync(bool engaged, string actor, CancellationToken cancellationToken) =>
        ControlPlaneHttp.SendAsync(http, HttpMethod.Post, "/admin/controls/emergency-stop", new { enabled = engaged, actor }, cancellationToken);

    public Task<ControlPlaneResponse> SetActionDisabledAsync(string actionKind, bool disabled, string actor, CancellationToken cancellationToken) =>
        ControlPlaneHttp.SendAsync(http, HttpMethod.Post, $"/admin/controls/actions/{Uri.EscapeDataString(actionKind)}", new { enabled = disabled, actor }, cancellationToken);
}

internal sealed class SourceHealthClient(HttpClient http) : ISourceHealth
{
    public Task<ControlPlaneResponse> SourcesAsync(CancellationToken cancellationToken) => ControlPlaneHttp.SendAsync(http, HttpMethod.Get, "/v1/sources", null, cancellationToken);
}

internal sealed class PostgresAuditLog(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : IAuditLog
{
    public async Task RecordAsync(string actor, string action, string target, object detail, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "insert into api.audit_log (actor, action, target, detail, occurred_at) values (@actor, @action, @target, @detail, @occurred_at)",
            current.Connection,
            current);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("target", target);
        command.Parameters.Add(new NpgsqlParameter("detail", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(detail, SentinelJson.Options) });
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditEntry>> RecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select sequence, actor, action, target, detail, occurred_at from api.audit_log order by sequence desc limit @limit");
        command.Parameters.AddWithValue("limit", limit);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<AuditEntry>();
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new AuditEntry(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                JsonDocument.Parse(reader.GetString(4)).RootElement.Clone(),
                await reader.GetFieldValueAsync<DateTimeOffset>(5, cancellationToken)));
        }

        return entries;
    }
}

internal sealed class RuntimeHistoryClient(HttpClient http) : IRuntimeHistory
{
    public Task<Postmortem?> PostmortemAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        SendOrNullAsync<Postmortem>(HttpMethod.Get, $"/v1/incidents/{incidentId}/postmortem", cancellationToken);

    public async Task<IReadOnlyList<SimilarIncident>?> SimilarAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        await SendOrNullAsync<List<SimilarIncident>>(HttpMethod.Get, $"/v1/incidents/{incidentId}/similar", cancellationToken);

    public Task<DomainReplayResult?> ReplayAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        SendOrNullAsync<DomainReplayResult>(HttpMethod.Post, $"/v1/incidents/{incidentId}/replays/domain", cancellationToken);

    private async Task<TValue?> SendOrNullAsync<TValue>(HttpMethod method, string uri, CancellationToken cancellationToken)
        where TValue : class
    {
        using var request = new HttpRequestMessage(method, new Uri(uri, UriKind.Relative));
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TValue>(SentinelJson.Options, cancellationToken);
    }
}

internal sealed class AgentExecutionsClient(HttpClient http) : IAgentExecutions
{
    public async Task<JsonElement> ForIncidentAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        await http.GetFromJsonAsync<JsonElement>($"/v1/executions?incidentId={incidentId}", SentinelJson.Options, cancellationToken);

    public async Task<JsonElement?> GetAsync(AgentExecutionId executionId, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(new Uri($"/v1/executions/{executionId}", UriKind.Relative), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(SentinelJson.Options, cancellationToken);
    }

    public async Task<AgentReplayResponse> ReplayAsync(AgentExecutionId executionId, InvestigationReplayRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync($"/v1/executions/{executionId}/replays", request, SentinelJson.Options, cancellationToken);
        return response.IsSuccessStatusCode
            ? new AgentReplayResponse(await response.Content.ReadFromJsonAsync<InvestigationReplayResult>(SentinelJson.Options, cancellationToken), (int)response.StatusCode, null)
            : new AgentReplayResponse(null, (int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }
}

/// <summary>Postmortem narratives with optimistic versioning: an edit saves only over the version it started from.</summary>
internal sealed class PostgresNarrativeStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : INarrativeStore
{
    public async Task<PostmortemNarrative?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select markdown, version, edited_by, edited_at from api.postmortem_narratives where incident_id = @incident_id");
        command.Parameters.AddWithValue("incident_id", incidentId.Value);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new PostmortemNarrative(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken))
            : null;
    }

    public async Task<bool> SaveAsync(IncidentId incidentId, PostmortemNarrative narrative, int expectedVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(narrative);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            expectedVersion == 0
                ? "insert into api.postmortem_narratives (incident_id, markdown, version, edited_by, edited_at) values (@incident_id, @markdown, @version, @edited_by, @edited_at) on conflict do nothing"
                : "update api.postmortem_narratives set markdown = @markdown, version = @version, edited_by = @edited_by, edited_at = @edited_at where incident_id = @incident_id and version = @expected",
            current.Connection,
            current);
        command.Parameters.AddWithValue("incident_id", incidentId.Value);
        command.Parameters.AddWithValue("markdown", narrative.Markdown);
        command.Parameters.AddWithValue("version", narrative.Version);
        command.Parameters.AddWithValue("edited_by", narrative.EditedBy);
        command.Parameters.AddWithValue("edited_at", narrative.EditedAt);
        if (expectedVersion != 0)
        {
            command.Parameters.AddWithValue("expected", expectedVersion);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
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
    /// <summary>Published versions of Api's control-plane document (Phase 08).</summary>
    public const string ConfigurationTable = "api.configuration_versions";

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

        create table if not exists api.postmortem_narratives (
            incident_id uuid primary key,
            markdown text not null,
            version integer not null,
            edited_by text not null,
            edited_at timestamptz not null
        );

        create table if not exists api.live_events (
            sequence bigserial primary key,
            tenant_id text not null,
            environment_id text not null,
            incident_id uuid not null,
            kind text not null,
            stream_version bigint not null,
            summary text not null,
            severity text,
            occurred_at timestamptz not null,
            recorded_at timestamptz not null default now()
        );

        create index if not exists live_events_by_tenant on api.live_events (tenant_id, sequence);

        create table if not exists api.notifications (
            notification_id uuid primary key,
            channel text not null,
            status text not null,
            attempts integer not null,
            next_attempt_at timestamptz not null,
            last_error text,
            message jsonb not null,
            created_at timestamptz not null
        );

        create index if not exists notifications_due on api.notifications (next_attempt_at) where status = 'Pending';

        create table if not exists api.audit_log (
            sequence bigserial primary key,
            actor text not null,
            action text not null,
            target text not null,
            detail jsonb not null,
            occurred_at timestamptz not null
        );

        create table if not exists api.idempotency_keys (
            actor text not null,
            key text not null,
            request_hash text not null,
            status_code integer,
            content_type text,
            body text,
            created_at timestamptz not null,
            completed_at timestamptz,
            primary key (actor, key)
        );

        create table if not exists api.analytics_daily (
            day date not null,
            event text not null,
            dimensions text not null,
            count bigint not null,
            primary key (day, event, dimensions)
        );
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(Sql + PostgresVersionedDocumentStore<ApiConfigurationDocument>.CreateTableSql(ConfigurationTable), connection, transaction);
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

/// <summary>
/// Idempotency keys in Api's database. A claim inserts the key, or takes over one older than a day;
/// otherwise the stored row decides between a replay, a mismatch and a request still running.
/// </summary>
internal sealed class PostgresIdempotencyStore(NpgsqlDataSource dataSource) : IIdempotencyStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    public async Task<IdempotencyClaim> ClaimAsync(string actor, string key, string requestHash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using (NpgsqlCommand claim = dataSource.CreateCommand(
            """
            insert into api.idempotency_keys (actor, key, request_hash, created_at) values (@actor, @key, @hash, @now)
            on conflict (actor, key) do update
                set request_hash = excluded.request_hash, created_at = excluded.created_at, status_code = null, content_type = null, body = null, completed_at = null
                where api.idempotency_keys.created_at < @expired
            returning 1
            """))
        {
            claim.Parameters.AddWithValue("actor", actor);
            claim.Parameters.AddWithValue("key", key);
            claim.Parameters.AddWithValue("hash", requestHash);
            claim.Parameters.AddWithValue("now", now);
            claim.Parameters.AddWithValue("expired", now - Lifetime);
            if (await claim.ExecuteScalarAsync(cancellationToken) is not null)
            {
                return new IdempotencyClaim.Claimed();
            }
        }

        await using NpgsqlCommand existing = dataSource.CreateCommand("select request_hash, status_code, content_type, body from api.idempotency_keys where actor = @actor and key = @key");
        existing.Parameters.AddWithValue("actor", actor);
        existing.Parameters.AddWithValue("key", key);
        await using NpgsqlDataReader reader = await existing.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            // Released between the two statements: the client may retry.
            return new IdempotencyClaim.InProgress();
        }

        if (reader.GetString(0) != requestHash)
        {
            return new IdempotencyClaim.Mismatch();
        }

        if (await reader.IsDBNullAsync(1, cancellationToken))
        {
            return new IdempotencyClaim.InProgress();
        }

        string? contentType = await reader.IsDBNullAsync(2, cancellationToken) ? null : reader.GetString(2);
        string body = await reader.IsDBNullAsync(3, cancellationToken) ? string.Empty : reader.GetString(3);
        return new IdempotencyClaim.Replay(new StoredResponse(reader.GetInt32(1), contentType, body));
    }

    public async Task CompleteAsync(string actor, string key, StoredResponse response, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        await using NpgsqlCommand command = dataSource.CreateCommand(
            "update api.idempotency_keys set status_code = @status, content_type = @content_type, body = @body, completed_at = @now where actor = @actor and key = @key");
        command.Parameters.AddWithValue("status", response.StatusCode);
        command.Parameters.AddWithValue("content_type", (object?)response.ContentType ?? DBNull.Value);
        command.Parameters.AddWithValue("body", response.Body);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Only an unfinished reservation is released; a recorded response stays for replays.
    public async Task ReleaseAsync(string actor, string key, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("delete from api.idempotency_keys where actor = @actor and key = @key and completed_at is null");
        command.Parameters.AddWithValue("actor", actor);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>Daily counts per event and property values; individual events are never stored.</summary>
internal sealed class PostgresAnalyticsStore(NpgsqlDataSource dataSource) : IAnalyticsStore
{
    public async Task IncrementAsync(DateOnly day, string eventName, string dimensions, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            """
            insert into api.analytics_daily (day, event, dimensions, count) values (@day, @event, @dimensions, 1)
            on conflict (day, event, dimensions) do update set count = api.analytics_daily.count + 1
            """);
        command.Parameters.AddWithValue("day", day);
        command.Parameters.AddWithValue("event", eventName);
        command.Parameters.AddWithValue("dimensions", dimensions);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnalyticsCount>> SinceAsync(DateOnly from, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select day, event, dimensions, count from api.analytics_daily where day >= @from order by day desc, event, dimensions");
        command.Parameters.AddWithValue("from", from);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var counts = new List<AnalyticsCount>();
        while (await reader.ReadAsync(cancellationToken))
        {
            counts.Add(new AnalyticsCount(await reader.GetFieldValueAsync<DateOnly>(0, cancellationToken), reader.GetString(1), reader.GetString(2), reader.GetInt64(3)));
        }

        return counts;
    }
}
