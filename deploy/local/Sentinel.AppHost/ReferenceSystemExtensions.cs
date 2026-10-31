using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Sentinel.AppHost;

internal static class ReferenceSystemExtensions
{
    private const string Prefix = "reference";

    /// <summary>
    /// Adds the Phase 01 reference system: its own Redis (separate from Sentinel's messaging Redis,
    /// ADR-008), the observability stack Sentinel ingests from, the reference services, the queue
    /// consumer with a Redis of its own for its queue (Phase E01), the deploy controller, a Redis
    /// client exporter and the scenario runner. Tokens are generated
    /// parameters kept in the AppHost's user secrets, never in source. With <paramref name="aws"/>
    /// the queue-consumer runs as an ECS task in the AWS environment instead, and the
    /// runner drives that environment (Phase E01).
    /// </summary>
    public static ReferenceSystemResources AddReferenceSystem(this IDistributedApplicationBuilder builder, bool aws = false)
    {
        string root = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "..", "reference-system"));
        var generated = new GenerateParameterDefault { MinLength = 32, Special = false };

        IResourceBuilder<ParameterResource> controlToken = builder.AddParameter($"{Prefix}-control-token", generated, secret: true, persist: true);
        IResourceBuilder<ParameterResource> deployToken = builder.AddParameter($"{Prefix}-deploy-token", generated, secret: true, persist: true);
        IResourceBuilder<ParameterResource> readToken = builder.AddParameter($"{Prefix}-read-token", generated, secret: true, persist: true);

        // maxclients is deliberately small so that S1's leak and S2's batch saturate it in about a minute.
        IResourceBuilder<ContainerResource> redis = builder.AddContainer($"{Prefix}-redis", "redis", "8.6")
            .WithEndpoint(targetPort: 6379, scheme: "tcp", name: "tcp")
            .WithArgs("--maxclients", "200", "--save", "", "--appendonly", "no");

        IResourceBuilder<ContainerResource> prometheus = builder.AddContainer("prometheus", "prom/prometheus", "v3.5.0")
            .WithContainerFiles("/etc/prometheus", Path.Combine(root, "deploy", "prometheus"))
            .WithArgs(
                "--config.file=/etc/prometheus/prometheus.yml",
                "--storage.tsdb.path=/prometheus",
                "--storage.tsdb.retention.time=2h",
                "--web.enable-otlp-receiver")
            .WithHttpEndpoint(targetPort: 9090, name: "http")
            .WithHttpHealthCheck("/-/ready");

        IResourceBuilder<ContainerResource> loki = builder.AddContainer("loki", "grafana/loki", "3.5.0")
            .WithContainerFiles("/etc/loki/reference", Path.Combine(root, "deploy", "loki"))
            .WithArgs("-config.file=/etc/loki/reference/loki.yaml")
            .WithHttpEndpoint(targetPort: 3100, name: "http")
            .WithHttpHealthCheck("/ready");

        IResourceBuilder<ContainerResource> jaeger = builder.AddContainer("jaeger", "jaegertracing/jaeger", "2.8.0")
            .WithHttpEndpoint(targetPort: 16686, name: "http")
            .WithHttpEndpoint(targetPort: 4318, name: "otlp")
            .WithHttpHealthCheck("/", endpointName: "http");

        IResourceBuilder<ProjectResource> orders = AddReferenceService<Projects.Reference_Orders>("order-service");
        IResourceBuilder<ProjectResource> checkout = AddReferenceService<Projects.Reference_Checkout>("checkout-service")
            .WithReference(orders)
            .WaitFor(orders);
        IResourceBuilder<ProjectResource> reporting = AddReferenceService<Projects.Reference_ReportingWorker>("reporting-worker");
        IResourceBuilder<ProjectResource>? queueConsumer = aws ? null : AddQueueConsumer();
        AddReferenceService<Projects.Reference_RedisExporter>("redis-exporter");

        IResourceBuilder<ProjectResource> deployController = AddReferenceService<Projects.Reference_DeployController_Api>("deploy-controller")
            .WithEnvironment("DeployController__DeployToken", deployToken)
            .WithEnvironment("DeployController__ReadToken", readToken)
            .WithReference(checkout)
            .WithReference(orders)
            .WithReference(reporting);

        IResourceBuilder<ProjectResource> scenarioRunner = AddReferenceService<Projects.Reference_Scenarios_Api>("scenario-runner")
            .WithEnvironment("DeployController__DeployToken", deployToken)
            .WithReference(checkout)
            .WithReference(orders)
            .WithReference(reporting)
            .WithReference(deployController)
            .WaitFor(checkout)
            .WaitFor(deployController);

        if (queueConsumer is not null)
        {
            deployController.WithReference(queueConsumer);
            scenarioRunner.WithReference(queueConsumer);
        }

        AwsReferenceEnvironmentResources? awsEnvironment = queueConsumer is null ? builder.AddAwsReferenceEnvironment(root, scenarioRunner) : null;

        // A scenario runs only in the environment its manifest names.
        foreach (string scenario in aws ? ["AWS-S0", "AWS-S1"] : new[] { "S0", "S1", "S2", "S3", "S6", "S7", "S8" })
        {
            scenarioRunner.WithHttpCommand("/api/runs", $"Run {scenario}", commandName: $"run-{scenario.ToLowerInvariant()}", commandOptions: Command(controlToken, $$"""{"scenarioId":"{{scenario}}"}"""));
        }

        scenarioRunner
            .WithHttpCommand("/api/reset", "Reset to healthy", commandName: "reset", commandOptions: Command(controlToken, null))
            .WithHttpCommand("/api/actions/stop-batch", "Stop reporting batch", commandName: "stop-batch", commandOptions: Command(controlToken, null));

        return new ReferenceSystemResources(deployController, scenarioRunner, reporting, queueConsumer, prometheus, loki, jaeger, deployToken, readToken, controlToken, Path.Combine(root, "knowledge"), awsEnvironment?.LocalAws);

        // The queue-consumer's queue lives apart from the shared Redis, as a managed queue lives apart from a cache, so that
        // S1's leak and S2's batch exhausting the shared Redis never back the queue up (S6).
        IResourceBuilder<ProjectResource> AddQueueConsumer()
        {
            IResourceBuilder<ContainerResource> queueRedis = builder.AddContainer($"{Prefix}-queue", "redis", "8.6")
                .WithEndpoint(targetPort: 6379, scheme: "tcp", name: "tcp")
                .WithArgs("--save", "", "--appendonly", "no");
            return AddReferenceService<Projects.Reference_QueueConsumer>("queue-consumer")
                .WithEnvironment("Reference__Redis", ReferenceExpression.Create($"{queueRedis.GetEndpoint("tcp").Property(EndpointProperty.HostAndPort)}"))
                .WithReference(orders)
                .WaitFor(queueRedis)
                .WaitFor(orders);
        }

        IResourceBuilder<ProjectResource> AddReferenceService<TProject>(string name)
            where TProject : IProjectMetadata, new() =>
            builder.AddProject<TProject>(name)
                .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
                .WithEnvironment("Reference__FailureControls__Enabled", "true")
                .WithEnvironment("Reference__ControlToken", controlToken)
                .WithEnvironment("Reference__Redis", ReferenceExpression.Create($"{redis.GetEndpoint("tcp").Property(EndpointProperty.HostAndPort)}"))
                .WithEnvironment("Reference__Telemetry__MetricsEndpoint", ReferenceExpression.Create($"{prometheus.GetEndpoint("http")}/api/v1/otlp/v1/metrics"))
                .WithEnvironment("Reference__Telemetry__LogsEndpoint", ReferenceExpression.Create($"{loki.GetEndpoint("http")}/otlp/v1/logs"))
                .WithEnvironment("Reference__Telemetry__TracesEndpoint", ReferenceExpression.Create($"{jaeger.GetEndpoint("otlp")}/v1/traces"))
                .WithHttpHealthCheck("/health")
                .WaitFor(redis);
    }

    private static HttpCommandOptions Command(IResourceBuilder<ParameterResource> controlToken, string? body) => new()
    {
        Method = HttpMethod.Post,
        PrepareRequest = async context =>
        {
            context.Request.Headers.Add("X-Reference-Control-Token", await controlToken.Resource.GetValueAsync(context.CancellationToken));
            if (body is not null)
            {
                context.Request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            }
        }
    };
}
