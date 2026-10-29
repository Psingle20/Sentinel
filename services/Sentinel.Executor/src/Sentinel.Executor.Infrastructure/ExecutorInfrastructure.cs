using System.Net.Http.Headers;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Infrastructure;

public static class ExecutorInfrastructure
{
    /// <summary>
    /// The execution store, policy, attestation verification, adapters and reconciliation. The
    /// deploy token comes from <c>Executor:Adapters:DeployController:Token</c>, supplied by secret
    /// configuration or the orchestrator: Executor is the only holder of privileged credentials.
    /// </summary>
    public static IServiceCollection AddExecutorInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ExecutorOptions>().Bind(configuration.GetSection(ExecutorOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMessagingStoreInitializer, ExecutorSchema>();
        services.AddScoped<IExecutionStore, PostgresExecutionStore>();
        services.AddScoped<IControlStore, PostgresControlStore>();
        services.AddSingleton<FilePolicyStore>();
        services.AddScoped<IPolicyStore, VersionedPolicyStore>();
        services.AddScoped<PolicyService>();
        services.AddScoped<PolicyAdministration>();
        services.AddScoped<AttestationVerifier>();
        services.AddScoped<ExecutionService>();

        Uri api = Required(configuration, "Executor:Services:Api");
        Uri runtime = Required(configuration, "Executor:Services:Runtime");
        Uri deployController = Required(configuration, "Executor:Adapters:DeployController:Endpoint");
        string deployToken = configuration["Executor:Adapters:DeployController:Token"] is { Length: > 0 } token
            ? token
            : throw new InvalidOperationException("Executor:Adapters:DeployController:Token is not configured. Supply it through secret configuration or the orchestrator.");

        // A singleton, so verified keys stay cached across requests.
        services.AddHttpClient(ApiApprovalKeys.ClientName, client => client.BaseAddress = api);
        services.AddSingleton<IApprovalKeyResolver, ApiApprovalKeys>();

        services.AddHttpClient<IStartConfirmation, RuntimeStartConfirmation>(client => client.BaseAddress = runtime);

        // The adapter decides about unknown outcomes itself (ADR-006), so the client does not retry.
#pragma warning disable EXTEXP0001
        services.AddHttpClient<IRemediationAdapter, DeployControllerRollbackAdapter>(client =>
        {
            client.BaseAddress = deployController;
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", deployToken);
        }).RemoveAllResilienceHandlers();

        // The batch adapter is optional: without the worker's endpoint and control token, pause-batch has no adapter and is refused.
        if (configuration["Executor:Adapters:ReportingWorker:Endpoint"] is { Length: > 0 } reporting
            && configuration["Executor:Adapters:ReportingWorker:ControlToken"] is { Length: > 0 } controlToken)
        {
            services.AddHttpClient<IRemediationAdapter, ReportingBatchAdapter>(client =>
            {
                client.BaseAddress = new Uri(reporting);
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Add(ReportingBatchAdapter.ControlHeader, controlToken);
            }).RemoveAllResilienceHandlers();
        }
#pragma warning restore EXTEXP0001

        services.AddHostedService<ReconciliationWorker>();
        return services;
    }

    private static Uri Required(IConfiguration configuration, string key) =>
        new(configuration[key] ?? throw new InvalidOperationException($"{key} is not configured."));
}

public sealed class ExecuteRemediationHandler(ExecutionService executions) : IIntegrationMessageHandler<ExecuteRemediation>
{
    public async Task<Result> HandleAsync(ExecuteRemediation message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        await executions.HandleAsync(message, cancellationToken);
        return Result.Success();
    }
}

/// <summary>Finishes requests that stopped part-way, for example because Executor was killed mid-execution.</summary>
internal sealed class ReconciliationWorker(IServiceScopeFactory scopes, IOptions<ExecutorOptions> options, TimeProvider time, ILogger<ReconciliationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<ExecutionRequest> unfinished;
                await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
                {
                    unfinished = await scope.ServiceProvider.GetRequiredService<IExecutionStore>().UnfinishedAsync(time.GetUtcNow() - options.Value.ReconcileAfter, stoppingToken);
                }

                foreach (ExecutionRequest request in unfinished)
                {
                    logger.LogWarning("Reconciling unfinished execution {Request}", ExecutionService.Describe(request));
                    await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ExecutionService>().ReconcileAsync(request, stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Reconciliation failed; it is retried on the next poll");
            }

            await Task.Delay(PollInterval, time, stoppingToken);
        }
    }
}

internal sealed class ExecutorSchema(NpgsqlDataSource dataSource) : IMessagingStoreInitializer
{
    private const string Sql = """
        select pg_advisory_xact_lock(hashtext('sentinel.executor.schema'));

        create schema if not exists executor;

        create table if not exists executor.execution_requests (
            request_id uuid primary key,
            idempotency_key text not null unique,
            incident_id uuid not null,
            state text not null,
            final boolean not null,
            snapshot jsonb not null,
            created_at timestamptz not null,
            updated_at timestamptz not null
        );

        create index if not exists execution_requests_unfinished on executor.execution_requests (updated_at) where not final;

        create table if not exists executor.consumed_attestations (
            attestation_id uuid primary key,
            request_id uuid not null,
            consumed_at timestamptz not null default now()
        );

        create table if not exists executor.incident_fences (
            incident_id uuid primary key,
            highest_token bigint not null
        );

        create table if not exists executor.capabilities (
            capability_id uuid primary key,
            request_id uuid not null,
            action text not null,
            resource text not null,
            scope text not null,
            issued_at timestamptz not null,
            expires_at timestamptz not null,
            consumed_at timestamptz,
            record jsonb not null
        );

        create table if not exists executor.policy_versions (
            version text primary key,
            document jsonb not null,
            author text not null,
            published_at timestamptz not null
        );

        create table if not exists executor.controls (
            control text primary key,
            value jsonb not null,
            changed_by text not null,
            changed_at timestamptz not null default now()
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

internal sealed class PostgresExecutionStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : IExecutionStore
{
    public async Task<ExecutionRequest?> FindAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select snapshot from executor.execution_requests where idempotency_key = @key");
        command.Parameters.AddWithValue("key", idempotencyKey);
        return await command.ExecuteScalarAsync(cancellationToken) is string json ? Restore(json) : null;
    }

    public async Task SaveAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into executor.execution_requests (request_id, idempotency_key, incident_id, state, final, snapshot, created_at, updated_at)
            values (@request_id, @key, @incident_id, @state, @final, @snapshot, @created_at, @updated_at)
            on conflict (request_id) do update set state = excluded.state, final = excluded.final, snapshot = excluded.snapshot, updated_at = excluded.updated_at
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("request_id", request.RequestId);
        command.Parameters.AddWithValue("key", request.IdempotencyKey);
        command.Parameters.AddWithValue("incident_id", request.IncidentId.Value);
        command.Parameters.AddWithValue("state", request.State.ToString());
        command.Parameters.AddWithValue("final", request.HasFinalOutcome);
        command.Parameters.Add(new NpgsqlParameter("snapshot", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request.Snapshot(), SentinelJson.Options) });
        command.Parameters.AddWithValue("created_at", request.CreatedAt);
        command.Parameters.AddWithValue("updated_at", request.UpdatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutionRequest>> UnfinishedAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select snapshot from executor.execution_requests where not final and updated_at < @older_than order by updated_at limit 20");
        command.Parameters.AddWithValue("older_than", olderThan);
        return await ReadAllAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutionRequest>> RecentAsync(int limit, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select snapshot from executor.execution_requests order by created_at desc limit @limit");
        command.Parameters.AddWithValue("limit", limit);
        return await ReadAllAsync(command, cancellationToken);
    }

    public async Task<bool> TryConsumeAttestationAsync(AttestationId attestationId, Guid requestId, CancellationToken cancellationToken)
    {
        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "insert into executor.consumed_attestations (attestation_id, request_id) values (@attestation_id, @request_id) on conflict do nothing",
            current.Connection,
            current);
        command.Parameters.AddWithValue("attestation_id", attestationId.Value);
        command.Parameters.AddWithValue("request_id", requestId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<bool> TryRaiseFenceAsync(IncidentId incidentId, long fencingToken, CancellationToken cancellationToken)
    {
        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into executor.incident_fences (incident_id, highest_token) values (@incident_id, @token)
            on conflict (incident_id) do update set highest_token = excluded.highest_token
                where executor.incident_fences.highest_token <= excluded.highest_token
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("incident_id", incidentId.Value);
        command.Parameters.AddWithValue("token", fencingToken);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task SaveCapabilityAsync(Capability capability, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(capability);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into executor.capabilities (capability_id, request_id, action, resource, scope, issued_at, expires_at, record)
            values (@id, @request_id, @action, @resource, @scope, @issued_at, @expires_at, @record)
            on conflict do nothing
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("id", capability.CapabilityId);
        command.Parameters.AddWithValue("request_id", capability.RequestId);
        command.Parameters.AddWithValue("action", capability.Action);
        command.Parameters.AddWithValue("resource", capability.Resource);
        command.Parameters.AddWithValue("scope", capability.Scope);
        command.Parameters.AddWithValue("issued_at", capability.IssuedAt);
        command.Parameters.AddWithValue("expires_at", capability.ExpiresAt);
        command.Parameters.Add(new NpgsqlParameter("record", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(capability, SentinelJson.Options) });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TryConsumeCapabilityAsync(Guid capabilityId, CancellationToken cancellationToken)
    {
        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "update executor.capabilities set consumed_at = now() where capability_id = @id and consumed_at is null and expires_at > now()",
            current.Connection,
            current);
        command.Parameters.AddWithValue("id", capabilityId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<IReadOnlyList<ExecutionRequest>> ReadAllAsync(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var requests = new List<ExecutionRequest>();
        while (await reader.ReadAsync(cancellationToken))
        {
            requests.Add(Restore(reader.GetString(0)));
        }

        return requests;
    }

    private static ExecutionRequest Restore(string json) =>
        ExecutionRequest.Restore(JsonSerializer.Deserialize<ExecutionSnapshot>(json, SentinelJson.Options)!);
}

/// <summary>Operator controls in Executor's own database, so an emergency stop survives restarts and needs no other service.</summary>
internal sealed class PostgresControlStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : IControlStore
{
    private const string EmergencyStop = "emergency-stop";
    private const string DisabledPrefix = "disabled:";

    public async Task<ExecutorControls> GetAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select control, value from executor.controls");
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        bool stopped = false;
        var disabled = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
        {
            string control = reader.GetString(0);
            bool on = JsonSerializer.Deserialize<bool>(reader.GetString(1));
            if (control == EmergencyStop)
            {
                stopped = on;
            }
            else if (control.StartsWith(DisabledPrefix, StringComparison.Ordinal) && on)
            {
                disabled.Add(control[DisabledPrefix.Length..]);
            }
        }

        return new ExecutorControls(stopped, disabled);
    }

    public Task SetEmergencyStopAsync(bool engaged, string actor, CancellationToken cancellationToken) => SetAsync(EmergencyStop, engaged, actor, cancellationToken);

    public Task SetActionDisabledAsync(string actionKind, bool disabled, string actor, CancellationToken cancellationToken) => SetAsync(DisabledPrefix + actionKind, disabled, actor, cancellationToken);

    private async Task SetAsync(string control, bool value, string actor, CancellationToken cancellationToken)
    {
        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into executor.controls (control, value, changed_by, changed_at) values (@control, @value, @actor, now())
            on conflict (control) do update set value = excluded.value, changed_by = excluded.changed_by, changed_at = excluded.changed_at
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("control", control);
        command.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(value) });
        command.Parameters.AddWithValue("actor", actor);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

/// <summary>
/// The current policy: the newest version published through the control plane (Phase 08), or the
/// repository's policy file when none has been. Published versions are kept with their author and
/// never edited, so every evaluation's version can be looked up later.
/// </summary>
internal sealed class VersionedPolicyStore(FilePolicyStore seed, NpgsqlDataSource dataSource) : IPolicyStore
{
    private const string UniqueViolation = "23505";

    public async Task<PolicyDocument> CurrentAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select document from executor.policy_versions order by published_at desc limit 1");
        return await command.ExecuteScalarAsync(cancellationToken) is string json
            ? JsonSerializer.Deserialize<PolicyDocument>(json, FilePolicyStore.Json)!
            : seed.Current;
    }

    public async Task<IReadOnlyList<PolicyVersionInfo>> HistoryAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select version, author, published_at from executor.policy_versions order by published_at desc");
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var history = new List<PolicyVersionInfo>();
        while (await reader.ReadAsync(cancellationToken))
        {
            history.Add(new PolicyVersionInfo(reader.GetString(0), reader.GetString(1), await reader.GetFieldValueAsync<DateTimeOffset>(2, cancellationToken), "control-plane"));
        }

        history.Add(new PolicyVersionInfo(seed.Current.Version, "repository", DateTimeOffset.MinValue, "file"));
        return history;
    }

    public async Task<bool> AppendAsync(PolicyDocument document, string author, DateTimeOffset publishedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        await using NpgsqlCommand command = dataSource.CreateCommand(
            "insert into executor.policy_versions (version, document, author, published_at) values (@version, @document, @author, @published_at)");
        command.Parameters.AddWithValue("version", document.Version);
        command.Parameters.Add(new NpgsqlParameter("document", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(document, FilePolicyStore.Json) });
        command.Parameters.AddWithValue("author", author);
        command.Parameters.AddWithValue("published_at", publishedAt);
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == UniqueViolation)
        {
            return false;
        }
    }
}

/// <summary>
/// The remediation policy as code: a JSON document beside the host, versioned in the repository and
/// reloaded when the file changes, so a policy change needs no rebuild and every evaluation names
/// the version it used. A document that fails to load keeps the previous one; with none, every action is denied.
/// It seeds the policy until the control plane publishes a version.
/// </summary>
internal sealed class FilePolicyStore(IOptions<ExecutorOptions> options, Microsoft.Extensions.Hosting.IHostEnvironment environment, ILogger<FilePolicyStore> logger)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static readonly PolicyDocument DenyAll = new() { Version = "deny-all", DefaultMode = AutonomyMode.ReadOnly };

    private readonly Lock _gate = new();
    private (DateTime Written, PolicyDocument Document)? _loaded;

    public PolicyDocument Current
    {
        get
        {
            string path = Path.Combine(environment.ContentRootPath, options.Value.PolicyFile);
            lock (_gate)
            {
                DateTime written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (_loaded is { } loaded && loaded.Written == written)
                {
                    return loaded.Document;
                }

                try
                {
                    PolicyDocument document = JsonSerializer.Deserialize<PolicyDocument>(File.ReadAllText(path), Json) ?? DenyAll;
                    _loaded = (written, document);
                    logger.LogInformation("Loaded remediation policy {Version} from {Path}", document.Version, path);
                    return document;
                }
                catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
                {
                    logger.LogError(exception, "The remediation policy at {Path} could not be loaded; keeping {Version}", path, _loaded?.Document.Version ?? DenyAll.Version);
                    return _loaded?.Document ?? DenyAll;
                }
            }
        }
    }
}
