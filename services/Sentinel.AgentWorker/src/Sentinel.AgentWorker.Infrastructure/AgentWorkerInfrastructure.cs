using System.Net.Http.Json;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Configuration;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Models;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.AgentWorker.Infrastructure.Models;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Infrastructure;

public static class AgentWorkerInfrastructure
{
    /// <summary>The options name for the deployment configuration without the control-plane document.</summary>
    public const string DeploymentOptions = "deployment";

    /// <summary>
    /// Tools, investigators, the model provider selected by <c>Model:Provider</c> (<c>test-double</c>
    /// by default, or <c>meta</c>) and the execution store. AgentWorker holds no provider
    /// credentials: every tool reads through Ingestion (ADR-007).
    /// </summary>
    public static IServiceCollection AddAgentWorkerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Each managed options type is also bound as "deployment": the control-plane document's seed, unchanged by it.
        services.AddOptions<InvestigationOptions>().Bind(configuration.GetSection(InvestigationOptions.Section));
        services.AddOptions<InvestigationOptions>(DeploymentOptions).Bind(configuration.GetSection(InvestigationOptions.Section));
        services.AddOptions<ModelRoutingOptions>().Bind(configuration.GetSection(ModelRoutingOptions.Section));
        services.AddOptions<ModelRoutingOptions>(DeploymentOptions).Bind(configuration.GetSection(ModelRoutingOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMessagingStoreInitializer, AgentWorkerSchema>();
        services.AddScoped<IExecutionStore, PostgresExecutionStore>();

        Uri ingestion = new(configuration["AgentWorker:Services:Ingestion"] ?? throw new InvalidOperationException("AgentWorker:Services:Ingestion is not configured."));
        services.AddOptions<CompletenessOptions>().Bind(configuration.GetSection(CompletenessOptions.Section));
        services.AddHttpClient<IngestionQueryClient>(client => client.BaseAddress = ingestion);

        // Every provider response a tool reads goes through the scope's capture: recorded with the
        // execution, and served from that record when the execution is replayed (Phase 07).
        services.AddScoped<InvestigationCapture>();
        services.AddScoped<IObservationQueries>(provider => new CapturingObservationQueries(
            new PatientObservationQueries(
                provider.GetRequiredService<IngestionQueryClient>(),
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CompletenessOptions>>(),
                provider.GetRequiredService<TimeProvider>()),
            provider.GetRequiredService<InvestigationCapture>()));
        services.AddHttpClient<IngestionKnowledgeClient>(client => client.BaseAddress = ingestion);
        services.AddScoped<IKnowledgeQueries>(provider => new CapturingKnowledgeQueries(provider.GetRequiredService<IngestionKnowledgeClient>(), provider.GetRequiredService<InvestigationCapture>()));
        services.AddScoped<IInvestigationTool, MetricRangeTool>();
        services.AddScoped<IInvestigationTool, RecentDeploymentsTool>();
        services.AddScoped<IInvestigationTool, LogSignaturesTool>();
        services.AddScoped<IInvestigationTool, SlowSpansTool>();
        services.AddScoped<IInvestigationTool, ServiceDependenciesTool>();
        services.AddScoped<IInvestigationTool, RunbookSearchTool>();
        services.AddScoped<IInvestigator, ModelInvestigator>();
        services.AddScoped<IInvestigator, DeployBlameInvestigator>();
        services.AddScoped<IInvestigator, AdversarialVerifier>();
        services.AddScoped<InvestigationService>();
        services.AddScoped<InvestigationReplayService>();

        services.AddOptions<ModelPricingOptions>().Bind(configuration.GetSection(ModelPricingOptions.Section));
        services.AddOptions<ModelPricingOptions>(DeploymentOptions).Bind(configuration.GetSection(ModelPricingOptions.Section));
        services.AddSingleton<AgentWorkerTelemetry>();

        // The test double is always available; the Meta provider only when configured (ADR-015).
        // Model:Provider picks the default, Model:Routing:<role> overrides it per role (Phase 04), and
        // the control plane can move roles between the providers registered here (Phase 08).
        services.AddOptions<TestDoubleOptions>().Bind(configuration.GetSection("Model:TestDouble"));
        services.AddSingleton<IModelProvider, DeterministicModelProvider>();
        string provider = configuration["Model:Provider"] ?? ModelRouter.TestDouble;
        bool metaRouted = provider == MetaModelProvider.ProviderName
            || configuration.GetSection("Model:Routing").GetChildren().Any(route => route.Value == MetaModelProvider.ProviderName);
        bool metaConfigured = configuration.GetSection("Model:Meta").Get<MetaModelOptions>()?.IsValid() == true;
        List<string> available = [ModelRouter.TestDouble];
        if (metaRouted || metaConfigured)
        {
            available.Add(MetaModelProvider.ProviderName);
            services.AddOptions<MetaModelOptions>()
                .Bind(configuration.GetSection("Model:Meta"))
                .Validate(options => options.IsValid(), "Model:Meta needs ChatCompletionsUri, Model and ApiKey (from secret configuration).")
                .ValidateOnStart();

            // Model calls run longer than the standard resilience timeouts allow, and the harness
            // decides about retries itself, so the default handlers are removed for this client.
#pragma warning disable EXTEXP0001
            services.AddHttpClient<MetaModelProvider>(client => client.Timeout = Timeout.InfiniteTimeSpan)
                .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
            services.AddTransient<IModelProvider>(services => services.GetRequiredService<MetaModelProvider>());
        }
        else if (provider != ModelRouter.TestDouble)
        {
            throw new InvalidOperationException($"Model:Provider '{provider}' is not supported; use {ModelRouter.TestDouble} or {MetaModelProvider.ProviderName}.");
        }

        services.AddScoped<IModelRouter>(services => new ModelRouter(services.GetServices<IModelProvider>(), services.GetRequiredService<IOptions<ModelRoutingOptions>>(), services.GetRequiredService<InvestigationCapture>()));

        // Phase 08: model selection through the control plane, applied over the deployment configuration.
        services.AddSingleton<IVersionedDocumentStore<ModelConfigurationDocument>>(provider =>
            new PostgresVersionedDocumentStore<ModelConfigurationDocument>(provider.GetRequiredService<NpgsqlDataSource>(), AgentWorkerSchema.ConfigurationTable, SentinelJson.Options));
        services.AddSingleton<VersionedDocumentPublisher<ModelConfigurationDocument>>();
        services.AddSingleton(provider => new ModelConfigurationService(
            provider.GetRequiredService<VersionedDocumentPublisher<ModelConfigurationDocument>>(),
            () => ModelConfigurationMapping.From(
                provider.GetRequiredService<IOptionsFactory<ModelRoutingOptions>>().Create(DeploymentOptions),
                provider.GetRequiredService<IOptionsFactory<InvestigationOptions>>().Create(DeploymentOptions),
                provider.GetRequiredService<IOptionsFactory<ModelPricingOptions>>().Create(DeploymentOptions)),
            available));
        services.AddManagedDocument<ModelRoutingOptions, ModelConfigurationDocument>(ModelConfigurationMapping.Apply);
        services.AddManagedDocument<InvestigationOptions, ModelConfigurationDocument>(ModelConfigurationMapping.Apply);
        services.AddManagedDocument<ModelPricingOptions, ModelConfigurationDocument>(ModelConfigurationMapping.Apply);
        return services;
    }
}

/// <summary>
/// Picks a provider per role: <c>Model:Routing:&lt;role&gt;</c> when set, otherwise <c>Model:Provider</c>.
/// Configuration values are <c>test-double</c> or <c>meta</c>. A level-3 replay's model profile
/// overrides both for its own scope only.
/// </summary>
internal sealed class ModelRouter(IEnumerable<IModelProvider> providers, IOptions<ModelRoutingOptions> routing, InvestigationCapture capture) : IModelRouter
{
    public const string TestDouble = ModelRoutingOptions.TestDouble;

    private readonly IReadOnlyList<IModelProvider> _providers = [.. providers];

    public IModelProvider For(string role)
    {
        string name = capture.ModelProfile ?? routing.Value.ProviderFor(role);
        string provider = name == TestDouble ? DeterministicModelProvider.ProviderName : name;
        return _providers.FirstOrDefault(candidate => candidate.Provider == provider)
            ?? throw new InvalidOperationException($"No model provider '{name}' is configured for the {role} role.");
    }
}

internal sealed class IngestionQueryClient(HttpClient http) : IObservationQueries
{
    public async Task<ObservationQueryResult> QueryAsync(ObservationQuery query, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("/v1/observations/query", query, SentinelJson.Options, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ObservationQueryResult>(SentinelJson.Options, cancellationToken))!;
    }
}

internal sealed class IngestionKnowledgeClient(HttpClient http) : IKnowledgeQueries
{
    public async Task<KnowledgeResult> SearchAsync(KnowledgeQuery query, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("/v1/knowledge/search", query, SentinelJson.Options, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KnowledgeResult>(SentinelJson.Options, cancellationToken))!;
    }
}

public sealed class InvestigateIncidentHandler(InvestigationService investigations) : IIntegrationMessageHandler<InvestigateIncident>
{
    public async Task<Result> HandleAsync(InvestigateIncident message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        await investigations.HandleAsync(message, cancellationToken);
        return Result.Success();
    }
}

internal sealed class AgentWorkerSchema(NpgsqlDataSource dataSource) : IMessagingStoreInitializer
{
    /// <summary>Published versions of AgentWorker's control-plane document (Phase 08).</summary>
    public const string ConfigurationTable = "agentworker.configuration_versions";

    private const string Sql = """
        select pg_advisory_xact_lock(hashtext('sentinel.agentworker.schema'));

        create schema if not exists agentworker;

        create table if not exists agentworker.executions (
            execution_id uuid primary key,
            tenant_id text not null,
            environment_id text not null,
            incident_id uuid not null,
            investigation_id uuid not null,
            investigator text not null,
            shadow boolean not null,
            result text not null,
            record jsonb not null,
            started_at timestamptz not null,
            completed_at timestamptz not null
        );

        create index if not exists executions_by_incident on agentworker.executions (incident_id, started_at);

        create table if not exists agentworker.execution_inputs (
            execution_id uuid primary key,
            command jsonb not null,
            responses jsonb not null,
            tool_catalog_version text not null,
            captured_at timestamptz not null
        );

        create table if not exists agentworker.replays (
            replay_id uuid primary key,
            execution_id uuid not null,
            level text not null,
            record jsonb not null,
            replayed_at timestamptz not null
        );

        create index if not exists replays_by_execution on agentworker.replays (execution_id, replayed_at);
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(Sql + PostgresVersionedDocumentStore<ModelConfigurationDocument>.CreateTableSql(ConfigurationTable), connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

internal sealed class PostgresExecutionStore(IPostgresTransaction transaction, NpgsqlDataSource dataSource) : IExecutionStore
{
    public async Task SaveAsync(AgentExecution execution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into agentworker.executions (execution_id, tenant_id, environment_id, incident_id, investigation_id, investigator, shadow, result, record, started_at, completed_at)
            values (@execution_id, @tenant_id, @environment_id, @incident_id, @investigation_id, @investigator, @shadow, @result, @record, @started_at, @completed_at)
            on conflict do nothing
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("execution_id", execution.AgentExecutionId.Value);
        command.Parameters.AddWithValue("tenant_id", execution.TenantId.Value);
        command.Parameters.AddWithValue("environment_id", execution.EnvironmentId.Value);
        command.Parameters.AddWithValue("incident_id", execution.IncidentId.Value);
        command.Parameters.AddWithValue("investigation_id", execution.InvestigationId.Value);
        command.Parameters.AddWithValue("investigator", execution.Investigator);
        command.Parameters.AddWithValue("shadow", execution.Shadow);
        command.Parameters.AddWithValue("result", execution.Result.ToString());
        command.Parameters.Add(new NpgsqlParameter("record", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(execution, SentinelJson.Options) });
        command.Parameters.AddWithValue("started_at", execution.StartedAt);
        command.Parameters.AddWithValue("completed_at", execution.CompletedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AgentExecution?> GetAsync(AgentExecutionId executionId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select record from agentworker.executions where execution_id = @execution_id");
        command.Parameters.AddWithValue("execution_id", executionId.Value);
        return await command.ExecuteScalarAsync(cancellationToken) is string json ? JsonSerializer.Deserialize<AgentExecution>(json, SentinelJson.Options) : null;
    }

    public async Task SaveInputsAsync(ExecutionInputs inputs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        NpgsqlTransaction current = await transaction.GetTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            insert into agentworker.execution_inputs (execution_id, command, responses, tool_catalog_version, captured_at)
            values (@execution_id, @command, @responses, @tool_catalog_version, @captured_at)
            on conflict do nothing
            """,
            current.Connection,
            current);
        command.Parameters.AddWithValue("execution_id", inputs.AgentExecutionId.Value);
        command.Parameters.Add(new NpgsqlParameter("command", NpgsqlDbType.Jsonb) { Value = inputs.Command });
        command.Parameters.Add(new NpgsqlParameter("responses", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(inputs.Responses, SentinelJson.Options) });
        command.Parameters.AddWithValue("tool_catalog_version", inputs.ToolCatalogVersion);
        command.Parameters.AddWithValue("captured_at", inputs.CapturedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ExecutionInputs?> GetInputsAsync(AgentExecutionId executionId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(
            "select command, responses, tool_catalog_version, captured_at from agentworker.execution_inputs where execution_id = @execution_id");
        command.Parameters.AddWithValue("execution_id", executionId.Value);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ExecutionInputs
        {
            AgentExecutionId = executionId,
            Command = reader.GetString(0),
            Responses = JsonSerializer.Deserialize<List<CapturedResponse>>(reader.GetString(1), SentinelJson.Options) ?? [],
            ToolCatalogVersion = reader.GetString(2),
            CapturedAt = await reader.GetFieldValueAsync<DateTimeOffset>(3, cancellationToken)
        };
    }

    /// <summary>Replays run outside any message handler, so each is committed on its own connection.</summary>
    public async Task SaveReplayAsync(Sentinel.Contracts.Incidents.InvestigationReplayResult replay, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replay);

        await using NpgsqlCommand command = dataSource.CreateCommand(
            "insert into agentworker.replays (replay_id, execution_id, level, record, replayed_at) values (@replay_id, @execution_id, @level, @record, @replayed_at) on conflict do nothing");
        command.Parameters.AddWithValue("replay_id", replay.ReplayId);
        command.Parameters.AddWithValue("execution_id", replay.AgentExecutionId.Value);
        command.Parameters.AddWithValue("level", replay.Level.ToString());
        command.Parameters.Add(new NpgsqlParameter("record", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(replay, SentinelJson.Options) });
        command.Parameters.AddWithValue("replayed_at", replay.ReplayedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentExecution>> ForIncidentAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand("select record from agentworker.executions where incident_id = @incident_id order by started_at");
        command.Parameters.AddWithValue("incident_id", incidentId.Value);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var executions = new List<AgentExecution>();
        while (await reader.ReadAsync(cancellationToken))
        {
            executions.Add(JsonSerializer.Deserialize<AgentExecution>(reader.GetString(0), SentinelJson.Options)!);
        }

        return executions;
    }
}
