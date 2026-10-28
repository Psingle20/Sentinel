using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Sentinel.AppHost;

// Matches ServiceDefaultsExtensions.ReadinessPath in Sentinel.ServiceDefaults.
const string ReadinessPath = "/health/ready";

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// End-to-end runs pass Sentinel:EphemeralStorage=true so that every run starts from empty databases.
bool ephemeral = builder.Configuration.GetValue<bool>("Sentinel:EphemeralStorage");

// The system Sentinel observes and remediates (Phase 01, ADR-011).
ReferenceSystemResources reference = builder.AddReferenceSystem();

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
    IResourceBuilder<ProjectResource> ingestion = AddService<Projects.Sentinel_Ingestion_Host>("ingestion")
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

    IResourceBuilder<ProjectResource> executor = AddService<Projects.Sentinel_Executor_Host>("executor");
    IResourceBuilder<ProjectResource> runtime = AddService<Projects.Sentinel_Runtime_Host>("runtime")
        .WithEnvironment("Runtime__Temporal__Address", ReferenceExpression.Create($"{temporal.GetEndpoint("grpc").Property(EndpointProperty.HostAndPort)}"))
        .WithReference(ingestion)
        .WithReference(executor)
        .WaitFor(temporal);
    IResourceBuilder<ProjectResource> api = AddService<Projects.Sentinel_Api_Host>("api")
        .WithReference(runtime);

    AddService<Projects.Sentinel_AgentWorker_Host>("agentworker")
        .WithReference(ingestion);

    // Only Executor holds the privileged deploy token (ADR-006). It confirms starts with Runtime and
    // verifies attestations with Api's published keys.
    executor
        .WithEnvironment("Executor__Adapters__DeployController__Token", reference.DeployToken)
        .WithEnvironment("Executor__Adapters__ReportingWorker__ControlToken", reference.ControlToken)
        .WithReference(reference.ReportingWorker)
        .WithReference(reference.DeployController)
        .WithReference(runtime)
        .WithReference(api);

    // The web app talks only to Sentinel.Api. Run `npm ci` in apps/Sentinel.Web once; the AppHost does not install packages.
    // Uses the stable JavaScript API; AddNextJsApp is still experimental (ASPIREJAVASCRIPT001).
    builder.AddJavaScriptApp("web", "../../../apps/Sentinel.Web", "dev")
        .WithNpm(install: false)
        .WithHttpEndpoint(env: "PORT")
        .WithReference(api)
        .WaitFor(api);

    // Each service gets its own database on the shared server (ADR-009) and the messaging Redis (ADR-008).
    IResourceBuilder<ProjectResource> AddService<TProject>(string name)
        where TProject : IProjectMetadata, new()
    {
        IResourceBuilder<PostgresDatabaseResource> database = postgres.AddDatabase($"{name}-db", name);

        return builder.AddProject<TProject>(name)
            .WithReference(database)
            .WaitFor(database)
            .WithReference(messaging)
            .WaitFor(messaging)
            .WithHttpHealthCheck(ReadinessPath);
    }
}
