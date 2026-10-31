using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Sentinel.AppHost;

// Matches ServiceDefaultsExtensions.ReadinessPath in Sentinel.ServiceDefaults.
const string ReadinessPath = "/health/ready";

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// End-to-end runs pass Sentinel:EphemeralStorage=true so that every run starts from empty databases.
bool ephemeral = builder.Configuration.GetValue<bool>("Sentinel:EphemeralStorage");

// End-to-end runs pass Sentinel:ModelProvider=test-double so that a model configured in a developer's user secrets is
// never called by the suite, whatever roles it routes. Unset, AgentWorker's own configuration decides (ADR-015).
string? modelProvider = builder.Configuration["Sentinel:ModelProvider"];

// The system Sentinel observes and remediates (Phase 01, ADR-011). Sentinel:ReferenceEnvironment=aws runs the
// queue-consumer in the AWS-emulated environment instead of locally (Phase E01, ADR-021).
string referenceEnvironment = builder.Configuration["Sentinel:ReferenceEnvironment"] ?? "local";
if (referenceEnvironment is not ("local" or "aws"))
{
    throw new InvalidOperationException($"Sentinel:ReferenceEnvironment is '{referenceEnvironment}'; it must be 'local' or 'aws'.");
}

ReferenceSystemResources reference = builder.AddReferenceSystem(aws: referenceEnvironment == "aws");

// Sentinel:ReferenceOnly=true starts the reference system alone, for its own scenario suite.
if (!builder.Configuration.GetValue<bool>("Sentinel:ReferenceOnly"))
{
    AddSentinel();
}

await using DistributedApplication app = builder.Build();

await app.RunAsync();

void AddSentinel()
{
    IResourceBuilder<PostgresServerResource> postgres = builder.AddPostgres("postgres");

    // Dedicated to Sentinel's messaging and never shared with the reference system (ADR-008).
    // AOF with fsync on every write: a message the outbox marked as published survives a Redis restart.
    IResourceBuilder<RedisResource> messaging = builder.AddRedis("messaging")
        .WithArgs("--appendonly", "yes", "--appendfsync", "always");

    if (!ephemeral)
    {
        postgres.WithDataVolume();
        messaging.WithDataVolume();
    }

    // Only Ingestion holds provider credentials (ADR-007): the telemetry backends and the deploy
    // controller's read token.
    IResourceBuilder<ProjectResource> ingestion = AddService<Projects.Sentinel_Ingestion_Api>("ingestion-service", database: "ingestion")
        .WithEnvironment("Ingestion__Providers__Prometheus__Endpoint", reference.Prometheus.GetEndpoint("http"))
        .WithEnvironment("Ingestion__Providers__Loki__Endpoint", reference.Loki.GetEndpoint("http"))
        .WithEnvironment("Ingestion__Providers__Jaeger__Endpoint", reference.Jaeger.GetEndpoint("http"))
        .WithEnvironment("Ingestion__Providers__DeployController__Token", reference.ReadToken)
        .WithEnvironment("Ingestion__Providers__Runbooks__Directory", reference.KnowledgeDirectory)
        .WithReference(reference.DeployController)
        .WaitFor(reference.Prometheus);

    // Temporal holds workflow progress only (ADR-005), in its own databases on the same server.
    IResourceBuilder<ContainerResource> temporal = builder.AddContainer("temporal", "temporalio/auto-setup", "1.28.1")
        .WithEnvironment("DB", "postgres12")
        .WithEnvironment("DB_PORT", "5432")
        .WithEnvironment("POSTGRES_SEEDS", postgres.Resource.Name)
        .WithEnvironment("POSTGRES_USER", postgres.Resource.UserNameReference)
        .WithEnvironment("POSTGRES_PWD", ReferenceExpression.Create($"{postgres.Resource.PasswordParameter}"))
        .WithEnvironment("DBNAME", "temporal")
        .WithEnvironment("VISIBILITY_DBNAME", "temporal_visibility")
        .WithEndpoint(targetPort: 7233, name: "grpc", scheme: "tcp")
        .WaitFor(postgres);

    IResourceBuilder<ProjectResource> executor = AddService<Projects.Sentinel_Executor_Api>("executor-service", database: "executor");
    IResourceBuilder<ProjectResource> runtime = AddService<Projects.Sentinel_Runtime_Api>("runtime-service", database: "runtime")
        .WithEnvironment("Runtime__Temporal__Address", ReferenceExpression.Create($"{temporal.GetEndpoint("grpc").Property(EndpointProperty.HostAndPort)}"))
        .WithReference(ingestion)
        .WithReference(executor)
        .WaitFor(temporal);
    IResourceBuilder<ProjectResource> agentworker = AddService<Projects.Sentinel_AgentWorker_Api>("agent-worker-service", database: "agentworker")
        .WithReference(ingestion);
    if (modelProvider is not null)
    {
        agentworker.WithEnvironment("Model__Override", modelProvider);
    }

    // The gateway reads incidents from Runtime and agent executions and replays from AgentWorker (Phase 07), and is the
    // control plane's edge for Executor's policy and controls and Ingestion's sources (Phase 08).
    IResourceBuilder<ProjectResource> gateway = AddService<Projects.Sentinel_Gateway_Api>("gateway-service", database: "api")
        .WithReference(runtime)
        .WithReference(agentworker)
        .WithReference(executor)
        .WithReference(ingestion);

    // Runtime verifies approver decisions against the gateway's published keys before recording them (ADR-006).
    runtime.WithReference(gateway);

    // Only Executor holds the privileged deploy token (ADR-006). It confirms starts with Runtime and
    // verifies attestations with the gateway's published keys.
    executor
        .WithEnvironment("Executor__Adapters__DeployController__Token", reference.DeployToken)
        .WithEnvironment("Executor__Adapters__ReportingWorker__ControlToken", reference.ControlToken)
        .WithEnvironment("Executor__Adapters__QueueConsumer__ControlToken", reference.ControlToken)
        .WithReference(reference.ReportingWorker)
        .WithReference(reference.DeployController)
        .WithReference(runtime)
        .WithReference(gateway);
    if (reference.QueueConsumer is { } queueConsumer)
    {
        executor.WithReference(queueConsumer);
    }

    if (reference.AwsEmulator is { } emulator)
    {
        AwsReferenceEnvironmentExtensions.WireSentinel(emulator, ingestion, agentworker, executor);
    }

    // The web app talks only to Sentinel.Gateway. Run `npm ci` in apps/Sentinel.Web once; the AppHost does not install packages.
    // Uses the stable JavaScript API; AddNextJsApp is still experimental (ASPIREJAVASCRIPT001).
    builder.AddJavaScriptApp("web", "../../../apps/Sentinel.Web", "dev")
        .WithNpm(install: false)
        .WithHttpEndpoint(env: "PORT")
        .WithReference(gateway)
        .WaitFor(gateway);

    // Each service gets its own database on the shared server (ADR-009) and the messaging Redis (ADR-008).
    // Database names are durable identities and do not follow a service's resource name (ADR-020).
    IResourceBuilder<ProjectResource> AddService<TProject>(string name, string? database = null)
        where TProject : IProjectMetadata, new()
    {
        string databaseName = database ?? name;
        IResourceBuilder<PostgresDatabaseResource> store = postgres.AddDatabase($"{databaseName}-db", databaseName);

        return builder.AddProject<TProject>(name)
            .WithReference(store)
            .WaitFor(store)
            .WithReference(messaging)
            .WaitFor(messaging)
            .WithHttpHealthCheck(ReadinessPath);
    }
}
