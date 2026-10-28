using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Providers;
using Sentinel.Ingestion.Domain.Observations;
using Sentinel.Ingestion.Infrastructure.Providers;
using Shouldly;

namespace Sentinel.Ingestion.Tests;

public sealed class RedactionTests
{
    [Theory]
    [InlineData("login failed for alice@example.test", "login failed for [redacted:email]")]
    [InlineData("Authorization: Bearer abcdefghijklmnop", "Authorization: Bearer [redacted:credential]")]
    [InlineData("retry with password=hunter2 now", "retry with password=[redacted:secret] now")]
    [InlineData("client 10.1.2.3 reset", "client [redacted:ip] reset")]
    [InlineData("token eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiJ4eXoifQ.c2lnbmF0dXJlLXZhbHVl expired", "token [redacted:token] expired")]
    public void Sensitive_values_are_redacted_before_storage(string text, string expected)
    {
        (string redacted, bool changed) = RedactionPolicy.Apply(text);

        redacted.ShouldBe(expected);
        changed.ShouldBeTrue();
    }

    [Fact]
    public void Ordinary_text_is_left_alone()
    {
        RedactionPolicy.Apply("Checkout cart-42 failed on Redis: max number of clients reached").Redacted.ShouldBeFalse();
    }

    [Fact]
    public void Lines_that_differ_only_in_values_share_a_signature()
    {
        LogSignature.Of("Checkout cart-1042 failed after 3 attempts")
            .ShouldBe(LogSignature.Of("Checkout cart-77 failed after 4 attempts"));
        LogSignature.Of("order 0192f0c4-0000-7000-8000-000000000001 placed")
            .ShouldBe("order <id> placed");
    }
}

public sealed class SignalAdapterTests
{
    private static readonly DateTimeOffset From = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = TenantId.From("acme");
    private static readonly EnvironmentId Environment = EnvironmentId.From("reference");

    [Fact]
    public async Task Loki_lines_become_redacted_log_observations_with_trace_context()
    {
        const string body = """
            {"status":"success","data":{"resultType":"streams","result":[
              {"stream":{"service_name":"checkout-service","severity_text":"Error","trace_id":"4bf92f3577b34da6a3ce929d0e0e4736","span_id":"00f067aa0ba902b7","exception_type":"RedisUnavailableException","exception_message":"auth failed for ops@example.test"},
               "values":[["1793178000123456789","Checkout cart-42 failed on Redis: connection refused by 10.0.0.5"]]},
              {"stream":{"service_name":"Bad Name"},"values":[["1793178000000000000","ignored"]]}
            ]}}
            """;
        var provider = new LokiLogsProvider(Client(body), TimeProvider.System, NullLogger<LokiLogsProvider>.Instance);
        var source = new LogSourceDefinition { Name = "errors", Query = "{service_name=~\".+\"} | severity_text=\"Error\"" };

        ProviderResult<LogObservation> result = await provider.QueryRangeAsync(new LogRangeRequest(Tenant, Environment, source, new TimeRange(From, From.AddSeconds(30))), CancellationToken.None);

        LogObservation log = result.Observations.ShouldHaveSingleItem();
        log.Severity.ShouldBe(LogSeverity.Error);
        log.Message.ShouldBe("Checkout cart-42 failed on Redis: connection refused by [redacted:ip]");
        log.ExceptionMessage.ShouldBe("auth failed for [redacted:email]");
        log.Attributes["redacted"].ShouldBe("true");
        log.Attributes["signature"].ShouldContain("cart-<n>");
        log.TraceContext!.TraceId.ShouldBe("4bf92f3577b34da6a3ce929d0e0e4736");
        log.EventTime.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1793178000123).AddTicks(4567));
        log.ObservationId.Value.ShouldStartWith("loki:errors:checkout-service:1793178000123456789:");
    }

    [Fact]
    public async Task Jaeger_spans_become_trace_observations_with_cross_service_dependencies()
    {
        const string body = """
            {"data":[{"traceID":"t1","spans":[
              {"traceID":"t1","spanID":"a","operationName":"POST /api/checkout","references":[],"startTime":1793178000000000,"duration":1800000,"tags":[{"key":"otel.status_code","value":"ERROR"}],"processID":"p1"},
              {"traceID":"t1","spanID":"b","operationName":"POST","references":[{"refType":"CHILD_OF","traceID":"t1","spanID":"a"}],"startTime":1793178000010000,"duration":600000,"tags":[],"processID":"p1"},
              {"traceID":"t1","spanID":"c","operationName":"POST /api/orders","references":[{"refType":"CHILD_OF","traceID":"t1","spanID":"b"}],"startTime":1793178000012000,"duration":590000,"tags":[],"processID":"p2"}
            ],"processes":{"p1":{"serviceName":"checkout-service"},"p2":{"serviceName":"order-service"}}}]}
            """;
        var provider = new JaegerProvider(Client(body), TimeProvider.System, NullLogger<JaegerProvider>.Instance);
        var source = new TraceSourceDefinition { Name = "checkout", Services = ["checkout-service"], SampleTraceLimit = 0 };

        ProviderResult<TraceObservation> result = await provider.QueryRangeAsync(new TraceRangeRequest(Tenant, Environment, source, new TimeRange(From, From.AddSeconds(30))), CancellationToken.None);

        result.Observations.Count.ShouldBe(3);
        TraceObservation root = result.Observations.Single(span => span.SpanId == "a");
        root.Status.ShouldBe(SpanStatus.Error);
        root.Duration.ShouldBe(TimeSpan.FromMilliseconds(1800));
        result.Observations.Single(span => span.SpanId == "b").DependencyServiceId.ShouldBe(ServiceId.From("order-service"));
        result.Observations.Single(span => span.SpanId == "c").ParentSpanId.ShouldBe("b");
    }

    [Fact]
    public async Task Jaeger_dependencies_become_topology_edges()
    {
        const string body = """{"data":[{"parent":"checkout-service","child":"order-service","callCount":120}]}""";
        var provider = new JaegerProvider(Client(body), TimeProvider.System, NullLogger<JaegerProvider>.Instance);

        ProviderResult<TopologyObservation> result = await provider.GetDependenciesAsync(new TopologyRequest(Tenant, Environment, new TimeRange(From, From.AddMinutes(15))), CancellationToken.None);

        TopologyObservation edge = result.Observations.ShouldHaveSingleItem();
        edge.ServiceId.ShouldBe(ServiceId.From("checkout-service"));
        edge.TargetServiceId.ShouldBe(ServiceId.From("order-service"));
        edge.Confidence.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_backend_is_an_unavailable_result()
    {
        var provider = new LokiLogsProvider(Client("not json", HttpStatusCode.ServiceUnavailable), TimeProvider.System, NullLogger<LokiLogsProvider>.Instance);

        ProviderResult<LogObservation> result = await provider.QueryRangeAsync(
            new LogRangeRequest(Tenant, Environment, new LogSourceDefinition { Name = "errors", Query = "{}" }, new TimeRange(From, From.AddSeconds(30))), CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Unavailable);
    }

    private static HttpClient Client(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new Stub(status, body)) { BaseAddress = new Uri("http://backend.test") };

    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
