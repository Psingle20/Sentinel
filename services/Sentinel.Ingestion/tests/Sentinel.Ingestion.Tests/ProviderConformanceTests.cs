using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
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
/// result rather than an exception or an empty success.
/// </summary>
public sealed class ProviderConformanceTests
{
    private static readonly DateTimeOffset From = DateTimeOffset.FromUnixTimeSeconds(1793178000);
    private static readonly TimeRange Range = new(From, From.AddSeconds(30));
    private static readonly TenantId Tenant = TenantId.From("acme");
    private static readonly EnvironmentId Environment = EnvironmentId.From("reference");

    public static TheoryData<string> Adapters => new() { "prometheus", "loki", "jaeger-traces", "jaeger-topology", "deploy-controller" };

    [Theory]
    [MemberData(nameof(Adapters))]
    public async Task Observations_carry_provenance_scope_and_deterministic_ids(string adapter)
    {
        AdapterCase subject = Case(adapter);

        (ProviderStatus status, IReadOnlyList<Observation> first) = await subject.Run(Client(HttpStatusCode.OK, subject.HealthyBody));
        (_, IReadOnlyList<Observation> second) = await subject.Run(Client(HttpStatusCode.OK, subject.HealthyBody));

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
    [MemberData(nameof(Adapters))]
    public async Task An_oversized_response_fails_the_pull_instead_of_growing_memory(string adapter)
    {
        AdapterCase subject = Case(adapter);
        HttpClient client = Client(HttpStatusCode.OK, subject.HealthyBody);
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
        _ => throw new ArgumentOutOfRangeException(nameof(adapter), adapter, "Unknown adapter.")
    };

    private static HttpClient Client(HttpStatusCode status, string body) =>
        new(new Stub(status, body)) { BaseAddress = new Uri("http://backend.test") };

    private sealed record AdapterCase(
        string Provider,
        string HealthyBody,
        Func<HttpClient, Task<(ProviderStatus Status, IReadOnlyList<Observation> Observations)>> Run,
        bool EventTimesInRange = true);

    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private sealed class Refusing : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Connection refused.");
    }
}
