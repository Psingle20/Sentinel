using System.Net;
using System.Text;
using System.Text.Json;
using Amazon.ECS;
using Amazon.EventBridge;
using Amazon.Runtime;
using Amazon.SQS;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Infrastructure.Providers;
using Shouldly;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using Sentinel.Ingestion.Application.Options;

namespace Sentinel.Ingestion.Tests;

/// <summary>
/// The adapter conformance suite (Phase 02, ADR-014). Every provider adapter must pass every rule, so
/// a new vendor is added by adding a case here, not by trusting its own tests: provenance reaches
/// the provider resource, IDs are deterministic, results are tenant-scoped and inside the requested
/// range, and an outage, throttling, malformed or oversized response is an explicit unavailable
/// result rather than an exception or an empty success. The AWS adapters of the AWS
/// environment (Phase E01) run through the AWS SDK over the same stubbed transport, answered per
/// operation; the SDK streams its responses, which the AWS APIs bound by page size, so the
/// buffer-size rule applies to the HTTP adapters alone.
/// </summary>
public sealed class ProviderConformanceTests
{
    private static readonly DateTimeOffset From = DateTimeOffset.FromUnixTimeSeconds(1793178000);
    private static readonly TimeRange Range = new(From, From.AddSeconds(30));
    private static readonly TenantId Tenant = TenantId.From("acme");
    private static readonly EnvironmentId Environment = EnvironmentId.From("reference");

    public static TheoryData<string> Adapters => new() { "prometheus", "prometheus-alerts", "loki", "jaeger-traces", "jaeger-topology", "deploy-controller", "ecs-deployments", "aws-topology", "sqs-metrics", "eventbridge-routes" };

    public static TheoryData<string> BufferedAdapters => new() { "prometheus", "prometheus-alerts", "loki", "jaeger-traces", "jaeger-topology", "deploy-controller" };

    private const string Cluster = "arn:aws:ecs:us-east-1:000000000000:cluster/reference";
    private const string Definition = "arn:aws:ecs:us-east-1:000000000000:task-definition/queue-consumer";
    private const string Queue = "http://local-aws:4566/000000000000/orders";
    private const string ApprovedRoute = """{"source":["reference.checkout"],"detail-type":["OrderPlaced"]}""";
    private const string DriftedRoute = """{"source":["reference.storefront"],"detail-type":["OrderPlaced"]}""";

    // One answer per AWS operation, as local AWS gives them: a queue-consumer service rolled out to revision 2, whose
    // task definition calls order-service and names its queue, that queue's attributes, and the rule that routes orders to
    // it, with a drifted pattern.
    private static string Aws(HttpRequestMessage request) => Aws(request, DriftedRoute);

    private static string Aws(HttpRequestMessage request, string routePattern)
    {
        string target = request.Headers.TryGetValues("X-Amz-Target", out IEnumerable<string>? values) ? values.Single() : string.Empty;
        string body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
        return target.Split('.')[^1] switch
        {
            "ListServices" => """{"serviceArns":["arn:aws:ecs:us-east-1:000000000000:service/reference/queue-consumer"]}""",
            "DescribeServices" => $$$"""{"services":[{"serviceName":"queue-consumer","clusterArn":"{{{Cluster}}}","status":"ACTIVE","taskDefinition":"{{{Definition}}}:2","createdAt":1793170000,"deployments":[{"id":"ecs-svc/2","status":"PRIMARY","taskDefinition":"{{{Definition}}}:2","createdAt":1793178005,"rolloutState":"COMPLETED"}]}],"failures":[]}""",
            "DescribeTaskDefinition" when body.Contains("queue-consumer:1", StringComparison.Ordinal) =>
                $$$"""{"taskDefinition":{"family":"queue-consumer","revision":1,"taskDefinitionArn":"{{{Definition}}}:1","containerDefinitions":[{"name":"consumer","image":"reference-queue-consumer:v1.4.0"}]}}""",
            "DescribeTaskDefinition" =>
                $$$"""{"taskDefinition":{"family":"queue-consumer","revision":2,"taskDefinitionArn":"{{{Definition}}}:2","containerDefinitions":[{"name":"consumer","image":"reference-queue-consumer:v1.4.1","environment":[{"name":"services__order-service__http__0","value":"http://host.docker.internal:5000"},{"name":"Reference__Queue__Sqs__QueueName","value":"orders"}]}]}}""",
            "ListQueues" => $$$"""{"QueueUrls":["{{{Queue}}}"]}""",
            "GetQueueUrl" => $$$"""{"QueueUrl":"{{{Queue}}}"}""",
            "GetQueueAttributes" => """{"Attributes":{"ApproximateNumberOfMessages":"2","ApproximateNumberOfMessagesNotVisible":"1"}}""",
            "ListRules" => $$$"""{"Rules":[{"Name":"orders-route","Arn":"arn:aws:events:us-east-1:000000000000:rule/orders-route","EventPattern":{{{JsonSerializer.Serialize(routePattern)}}},"State":"ENABLED","EventBusName":"default"}]}""",
            "ListTargetsByRule" => """{"Targets":[{"Id":"orders","Arn":"arn:aws:sqs:us-east-1:000000000000:orders"}]}""",
            _ => throw new InvalidOperationException($"No answer for {target}.")
        };
    }

    private const string Alerts = """
        {"status":"success","data":{"alerts":[
          {"labels":{"alertname":"CheckoutSloFastBurn","service":"checkout-service","severity":"critical"},"annotations":{"summary":"Checkout burns 14.8 times too fast; page oncall@example.test"},"state":"firing","activeAt":"2026-10-28T09:00:05Z","value":"1.48e+01"},
          {"labels":{"alertname":"RedisClientsNearLimit","service":"redis","severity":"warning"},"annotations":{"summary":"Redis has used 92% of its client connections."},"state":"pending","activeAt":"2026-10-28T09:00:10Z","value":"0.92"},
          {"labels":{"alertname":"Watchdog"},"annotations":{},"state":"firing","activeAt":"2026-10-28T08:00:00Z","value":"1"},
          {"labels":{"alertname":"OrdersSlow","service":"order-service"},"annotations":{},"state":"inactive","activeAt":"2026-10-28T09:00:00Z","value":"0"}
        ]}}
        """;

    [Fact]
    public async Task Alerts_keep_their_rule_state_severity_and_value_and_skip_what_names_no_service()
    {
        IReadOnlyList<AlertObservation> alerts = await AlertsAsync(Alerts);

        alerts.Select(alert => alert.AlertName).ShouldBe(["CheckoutSloFastBurn", "RedisClientsNearLimit"], "an alert with no service label or no longer active is not an observation");
        AlertObservation burn = alerts[0];
        burn.ServiceId.Value.ShouldBe("checkout-service");
        burn.State.ShouldBe("firing");
        burn.Severity.ShouldBe("critical");
        burn.Value.ShouldBe(14.8);
        burn.EventTime.ShouldBe(burn.ActiveAt);
        burn.SourceResource.ShouldBe("alerts/CheckoutSloFastBurn");
        burn.Summary.ShouldBe("Checkout burns 14.8 times too fast; page [redacted:email]");
        burn.Attributes["redacted"].ShouldBe("true");
        burn.Attributes.ShouldNotContainKey("alertname");
        alerts[1].State.ShouldBe("pending");
    }

    [Fact]
    public async Task A_pending_alert_that_starts_firing_is_a_new_observation()
    {
        IReadOnlyList<AlertObservation> pending = await AlertsAsync(Alerts);
        IReadOnlyList<AlertObservation> firing = await AlertsAsync(Alerts.Replace("\"state\":\"pending\"", "\"state\":\"firing\"", StringComparison.Ordinal));

        firing[1].ObservationId.ShouldNotBe(pending[1].ObservationId);
        firing[0].ObservationId.ShouldBe(pending[0].ObservationId, "an alert still firing adds nothing");
    }

    private static async Task<IReadOnlyList<AlertObservation>> AlertsAsync(string body)
    {
        var provider = new PrometheusAlertsProvider(Client(HttpStatusCode.OK, body), TimeProvider.System, NullLogger<PrometheusAlertsProvider>.Instance);
        ProviderResult<AlertObservation> result = await provider.GetActiveAlertsAsync(new AlertRequest(Tenant, Environment, From), CancellationToken.None);
        result.Status.ShouldBe(ProviderStatus.Healthy);
        return result.Observations;
    }

    [Theory]
    [MemberData(nameof(Adapters))]
    public async Task Observations_carry_provenance_scope_and_deterministic_ids(string adapter)
    {
        AdapterCase subject = Case(adapter);

        (ProviderStatus status, IReadOnlyList<Observation> first) = await subject.Run(Client(HttpStatusCode.OK, subject.Answer));
        (_, IReadOnlyList<Observation> second) = await subject.Run(Client(HttpStatusCode.OK, subject.Answer));

        status.ShouldBe(ProviderStatus.Healthy);
        first.ShouldNotBeEmpty();
        first.Select(observation => observation.ObservationId).ShouldBe(second.Select(observation => observation.ObservationId));
        first.Select(observation => observation.ObservationId).ShouldBeUnique();
        first.ShouldAllBe(observation => observation.TenantId == Tenant && observation.EnvironmentId == Environment);
        first.ShouldAllBe(observation => observation.SourceProvider == subject.Provider && !string.IsNullOrWhiteSpace(observation.SourceResource));
        first.ShouldAllBe(observation => observation.ObservationId.Value.StartsWith(subject.Provider + ":", StringComparison.Ordinal));
        if (subject.EventTimesInRange)
        {
            first.ShouldAllBe(observation => observation.EventTime >= Range.From && observation.EventTime < Range.To);
        }
    }

    [Theory]
    [MemberData(nameof(Adapters))]
    public async Task An_outage_is_an_unavailable_result_never_an_exception(string adapter)
    {
        AdapterCase subject = Case(adapter);

        (await subject.Run(Client(HttpStatusCode.ServiceUnavailable, "upstream down"))).Status.ShouldBe(ProviderStatus.Unavailable);
        (await subject.Run(new HttpClient(new Refusing()) { BaseAddress = new Uri("http://backend.test") })).Status.ShouldBe(ProviderStatus.Unavailable);
    }

    [Theory]
    [MemberData(nameof(Adapters))]
    public async Task Throttling_is_an_unavailable_result_so_the_source_backs_off(string adapter)
    {
        AdapterCase subject = Case(adapter);

        (ProviderStatus status, IReadOnlyList<Observation> observations) = await subject.Run(Client((HttpStatusCode)429, """{"error":"rate limited"}"""));

        status.ShouldBe(ProviderStatus.Unavailable);
        observations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Adapters))]
    public async Task A_malformed_response_is_an_unavailable_result(string adapter)
    {
        (await Case(adapter).Run(Client(HttpStatusCode.OK, "{ not json"))).Status.ShouldBe(ProviderStatus.Unavailable);
    }

    [Theory]
    [MemberData(nameof(BufferedAdapters))]
    public async Task An_oversized_response_fails_the_pull_instead_of_growing_memory(string adapter)
    {
        AdapterCase subject = Case(adapter);
        HttpClient client = Client(HttpStatusCode.OK, subject.Answer);
        client.MaxResponseContentBufferSize = 16;

        (await subject.Run(client)).Status.ShouldBe(ProviderStatus.Unavailable);
    }

    private static AdapterCase Case(string adapter) => adapter switch
    {
        "prometheus" => new AdapterCase(
            PrometheusMetricsProvider.ProviderName,
            """
            {"status":"success","data":{"resultType":"matrix","result":[
              {"metric":{"job":"checkout-service"},"values":[[1793178000,"0.4"],[1793178005,"0.6"]]},
              {"metric":{"job":"order-service"},"values":[[1793178000,"0.07"]]}
            ]}}
            """,
            async client =>
            {
                var provider = new PrometheusMetricsProvider(client, TimeProvider.System, NullLogger<PrometheusMetricsProvider>.Instance);
                var series = new MetricSeriesDefinition { Name = "latency_p95", Expression = "histogram_quantile(0.95, x)", ServiceLabel = "job" };
                ProviderResult<MetricObservation> result = await provider.QueryRangeAsync(new MetricRangeRequest(Tenant, Environment, series, Range, TimeSpan.FromSeconds(5)), CancellationToken.None);
                return (result.Status, result.Observations);
            }),
        "prometheus-alerts" => new AdapterCase(
            PrometheusMetricsProvider.ProviderName,
            Alerts,
            async client =>
            {
                var provider = new PrometheusAlertsProvider(client, TimeProvider.System, NullLogger<PrometheusAlertsProvider>.Instance);
                ProviderResult<AlertObservation> result = await provider.GetActiveAlertsAsync(new AlertRequest(Tenant, Environment, From), CancellationToken.None);
                return (result.Status, result.Observations);
            },
            EventTimesInRange: false),
        "loki" => new AdapterCase(
            LokiLogsProvider.ProviderName,
            """
            {"status":"success","data":{"resultType":"streams","result":[
              {"stream":{"service_name":"checkout-service","severity_text":"Warning"},"values":[["1793178001000000000","Order call attempt 1 timed out"],["1793178002000000000","Order call attempt 2 timed out"]]}
            ]}}
            """,
            async client =>
            {
                var provider = new LokiLogsProvider(client, TimeProvider.System, NullLogger<LokiLogsProvider>.Instance);
                ProviderResult<LogObservation> result = await provider.QueryRangeAsync(new LogRangeRequest(Tenant, Environment, new LogSourceDefinition { Name = "warnings", Query = "{service_name=~\".+\"}" }, Range), CancellationToken.None);
                return (result.Status, result.Observations);
            }),
        "jaeger-traces" => new AdapterCase(
            JaegerProvider.ProviderName,
            """
            {"data":[{"traceID":"t1","spans":[
              {"traceID":"t1","spanID":"a","operationName":"POST /api/checkout","references":[],"startTime":1793178001000000,"duration":900000,"tags":[],"processID":"p1"}
            ],"processes":{"p1":{"serviceName":"checkout-service"}}}]}
            """,
            async client =>
            {
                var provider = new JaegerProvider(client, TimeProvider.System, NullLogger<JaegerProvider>.Instance);
                ProviderResult<TraceObservation> result = await provider.QueryRangeAsync(new TraceRangeRequest(Tenant, Environment, new TraceSourceDefinition { Name = "path", Services = ["checkout-service"], SampleTraceLimit = 0 }, Range), CancellationToken.None);
                return (result.Status, result.Observations);
            }),
        "jaeger-topology" => new AdapterCase(
            JaegerProvider.ProviderName,
            """{"data":[{"parent":"checkout-service","child":"order-service","callCount":12}]}""",
            async client =>
            {
                var provider = new JaegerProvider(client, TimeProvider.System, NullLogger<JaegerProvider>.Instance);
                ProviderResult<TopologyObservation> result = await provider.GetDependenciesAsync(new TopologyRequest(Tenant, Environment, Range), CancellationToken.None);
                return (result.Status, result.Observations);
            },
            EventTimesInRange: false),
        "deploy-controller" => new AdapterCase(
            DeployControllerProvider.ProviderName,
            """
            [{"deploymentId":"d1","service":"checkout-service","version":"v2.4.1","previousVersion":"v2.4.0","kind":"deploy","actor":"ci","startedAt":"2026-10-28T09:00:01Z","completedAt":"2026-10-28T09:00:21Z","status":"completed"}]
            """,
            async client =>
            {
                var provider = new DeployControllerProvider(client, TimeProvider.System, NullLogger<DeployControllerProvider>.Instance);
                ProviderResult<DeploymentObservation> result = await provider.GetDeploymentsAsync(new DeploymentRequest(Tenant, Environment, new TimeRange(DateTimeOffset.Parse("2026-10-28T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.Parse("2026-10-28T09:01:00Z", System.Globalization.CultureInfo.InvariantCulture))), CancellationToken.None);
                return (result.Status, result.Observations);
            },
            EventTimesInRange: false),
        "ecs-deployments" => new AdapterCase(
            EcsDeploymentProvider.ProviderName,
            string.Empty,
            async client =>
            {
                var provider = new EcsDeploymentProvider(Ecs(client), AwsOptions, TimeProvider.System, NullLogger<EcsDeploymentProvider>.Instance);
                ProviderResult<DeploymentObservation> result = await provider.GetDeploymentsAsync(new DeploymentRequest(Tenant, Environment, Range), CancellationToken.None);
                return (result.Status, result.Observations);
            },
            EventTimesInRange: false,
            Route: Aws),
        "aws-topology" => new AdapterCase(
            AwsTopologyProvider.ProviderName,
            string.Empty,
            async client =>
            {
                var provider = new AwsTopologyProvider(Ecs(client), Sqs(client), AwsOptions, TimeProvider.System, NullLogger<AwsTopologyProvider>.Instance);
                ProviderResult<TopologyObservation> result = await provider.GetDependenciesAsync(new TopologyRequest(Tenant, Environment, Range), CancellationToken.None);
                return (result.Status, result.Observations);
            },
            EventTimesInRange: false,
            Route: Aws),
        "sqs-metrics" => new AdapterCase(
            SqsQueueMetricsProvider.ProviderName,
            string.Empty,
            async client =>
            {
                // SQS keeps no history, so a sample is stamped at the step of its reading, not inside the requested range.
                var provider = new SqsQueueMetricsProvider(Sqs(client), new FakeTimeProvider(From.AddMinutes(10)), NullLogger<SqsQueueMetricsProvider>.Instance);
                var series = new MetricSeriesDefinition { Name = "queue_depth", Expression = "orders:ApproximateNumberOfMessages+ApproximateNumberOfMessagesNotVisible", Service = "queue-consumer", Provider = SqsQueueMetricsProvider.ProviderName };
                ProviderResult<MetricObservation> result = await provider.QueryRangeAsync(new MetricRangeRequest(Tenant, Environment, series, Range, TimeSpan.FromSeconds(5)), CancellationToken.None);
                return (result.Status, result.Observations);
            },
            EventTimesInRange: false,
            Route: Aws),
        "eventbridge-routes" => new AdapterCase(
            EventBridgeRouteProvider.ProviderName,
            string.Empty,
            async client =>
            {
                // EventBridge keeps no history, so the provider's first pull, of the approved pattern, is its baseline; the
                // suite's transport answers the second, which sees the drifted one.
                using var transport = new Baseline(client);
                using var http = new HttpClient(transport) { BaseAddress = new Uri("http://backend.test") };
                var provider = new EventBridgeRouteProvider(Events(http), Ecs(http), AwsOptions, new FakeTimeProvider(From.AddMinutes(10)), NullLogger<EventBridgeRouteProvider>.Instance);
                var request = new DeploymentRequest(Tenant, Environment, new TimeRange(From, From.AddMinutes(20)));
                await provider.GetDeploymentsAsync(request, CancellationToken.None);
                transport.Forward = true;
                ProviderResult<DeploymentObservation> result = await provider.GetDeploymentsAsync(request, CancellationToken.None);
                return (result.Status, result.Observations);
            },
            EventTimesInRange: false,
            Route: Aws),
        _ => throw new ArgumentOutOfRangeException(nameof(adapter), adapter, "Unknown adapter.")
    };

    private static AwsProviderOptions AwsOptions => new() { ServiceUrl = new Uri("http://backend.test"), AccessKey = "000000000000", SecretKey = "test-not-a-secret", Clusters = ["reference"] };

    private static AmazonECSClient Ecs(HttpClient client) =>
        new(new BasicAWSCredentials(AwsOptions.AccessKey, AwsOptions.SecretKey), new AmazonECSConfig { ServiceURL = "http://backend.test", AuthenticationRegion = "us-east-1", MaxErrorRetry = 0, HttpClientFactory = new Transport(client) });

    private static AmazonEventBridgeClient Events(HttpClient client) =>
        new(new BasicAWSCredentials(AwsOptions.AccessKey, AwsOptions.SecretKey), new AmazonEventBridgeConfig { ServiceURL = "http://backend.test", AuthenticationRegion = "us-east-1", MaxErrorRetry = 0, HttpClientFactory = new Transport(client) });

    private static AmazonSQSClient Sqs(HttpClient client) =>
        new(new BasicAWSCredentials(AwsOptions.AccessKey, AwsOptions.SecretKey), new AmazonSQSConfig { ServiceURL = "http://backend.test", AuthenticationRegion = "us-east-1", MaxErrorRetry = 0, HttpClientFactory = new Transport(client) });

    private static HttpClient Client(HttpStatusCode status, string body) => Client(status, _ => body);

    private static HttpClient Client(HttpStatusCode status, Func<HttpRequestMessage, string> body) =>
        new(new Stub(status, body)) { BaseAddress = new Uri("http://backend.test") };

    private sealed record AdapterCase(
        string Provider,
        string HealthyBody,
        Func<HttpClient, Task<(ProviderStatus Status, IReadOnlyList<Observation> Observations)>> Run,
        bool EventTimesInRange = true,
        Func<HttpRequestMessage, string>? Route = null)
    {
        public Func<HttpRequestMessage, string> Answer => Route ?? (_ => HealthyBody);
    }

    private sealed class Stub(HttpStatusCode status, Func<HttpRequestMessage, string> body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body(request), Encoding.UTF8, "application/json") });
    }

    // Answers a provider's first pull itself, with the approved route, and forwards every later request to the suite's client.
    private sealed class Baseline(HttpClient suite) : HttpMessageHandler
    {
        public bool Forward { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!Forward)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Aws(request, ApprovedRoute), Encoding.UTF8, "application/json") };
            }

            using var copy = new HttpRequestMessage(request.Method, request.RequestUri);
            if (request.Content is not null)
            {
                copy.Content = new StringContent(await request.Content.ReadAsStringAsync(cancellationToken), Encoding.UTF8, request.Content.Headers.ContentType?.MediaType ?? "application/json");
            }

            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
            {
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return await suite.SendAsync(copy, cancellationToken);
        }
    }

    // Hands the AWS SDK the stubbed client, so the AWS adapters meet the same transport faults as the HTTP ones.
    private sealed class Transport(HttpClient client) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => client;

        public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => false;

        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => false;
    }

    private sealed class Refusing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused.");
    }
}
