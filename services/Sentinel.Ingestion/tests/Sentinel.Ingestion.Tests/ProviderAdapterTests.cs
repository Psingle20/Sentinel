using System.Net;
using System.Text;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Sentinel.Contracts;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Abstractions.Diagnostics;
using Sentinel.Ingestion.Application.Abstractions.Persistence;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using Sentinel.Ingestion.Application.BusinessTasks.Queries;
using Sentinel.Ingestion.Application.BusinessTasks.Queries.Observations;
using Sentinel.Ingestion.Application.Options;
using Sentinel.Ingestion.Application.Services;
using Sentinel.Ingestion.Domain.Sources;
using Sentinel.Ingestion.Infrastructure.Providers;
using Shouldly;

namespace Sentinel.Ingestion.Tests;

public sealed class ProviderAdapterTests
{
    private static readonly DateTimeOffset From = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Prometheus_series_become_metric_observations_with_services_from_labels()
    {
        const string body = """
            {"status":"success","data":{"resultType":"matrix","result":[
              {"metric":{"client_name":"checkout-service"},"values":[[1793178000,"4"],[1793178005,"NaN"],[1793178010,"40"]]},
              {"metric":{"client_name":"Not A Service!"},"values":[[1793178000,"1"]]}
            ]}}
            """;
        var handler = new StubHandler(HttpStatusCode.OK, body);
        var provider = new PrometheusMetricsProvider(new HttpClient(handler) { BaseAddress = new Uri("http://prometheus.test") }, TimeProvider.System, NullLogger<PrometheusMetricsProvider>.Instance);
        var series = new MetricSeriesDefinition { Name = "redis_connections", Expression = "sum by (client_name) (reference_redis_clients)", ServiceLabel = "client_name" };

        ProviderResult<MetricObservation> result = await provider.QueryRangeAsync(
            new MetricRangeRequest(TenantId.From("acme"), EnvironmentId.From("reference"), series, new TimeRange(From, From.AddSeconds(15)), TimeSpan.FromSeconds(5)),
            CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Healthy);
        result.Observations.Select(observation => observation.Value).ShouldBe([4, 40]);
        result.Observations.ShouldAllBe(observation => observation.ServiceId == ServiceId.From("checkout-service"));
        result.Observations[0].ObservationId.Value.ShouldBe("prometheus:redis_connections:checkout-service:1793178000");
        handler.LastRequest!.RequestUri!.Query.ShouldContain("step=5");
    }

    [Fact]
    public async Task A_Prometheus_error_is_an_unavailable_result_not_an_empty_success()
    {
        var provider = new PrometheusMetricsProvider(
            new HttpClient(new StubHandler(HttpStatusCode.BadRequest, """{"status":"error","error":"parse error"}""")) { BaseAddress = new Uri("http://prometheus.test") },
            TimeProvider.System,
            NullLogger<PrometheusMetricsProvider>.Instance);

        ProviderResult<MetricObservation> result = await provider.QueryRangeAsync(
            new MetricRangeRequest(TenantId.From("acme"), EnvironmentId.From("reference"), new MetricSeriesDefinition { Name = "x", Expression = "(", Service = "checkout-service" }, new TimeRange(From, From.AddSeconds(15)), TimeSpan.FromSeconds(5)),
            CancellationToken.None);

        result.Status.ShouldBe(ProviderStatus.Unavailable);
        result.Error!.ShouldContain("parse error");
    }

    [Fact]
    public async Task A_deployment_yields_a_started_and_a_completed_observation_and_a_config_change_yields_one()
    {
        const string body = """
            [
              {"deploymentId":"d1","service":"checkout-service","version":"v2.4.1","previousVersion":"v2.4.0","kind":"deploy","actor":"ci","startedAt":"2026-10-28T09:00:01Z","completedAt":"2026-10-28T09:00:21Z","status":"completed"},
              {"deploymentId":"c1","service":"order-service","version":"v1.7.0","previousVersion":"v1.7.0","kind":"configChange","actor":"ci","configurationDiff":"log level","startedAt":"2026-10-28T09:00:30Z","completedAt":"2026-10-28T09:00:30Z","status":"completed"}
            ]
            """;
        var provider = new DeployControllerProvider(new HttpClient(new StubHandler(HttpStatusCode.OK, body)) { BaseAddress = new Uri("http://deploy-controller.test") }, TimeProvider.System, NullLogger<DeployControllerProvider>.Instance);

        ProviderResult<DeploymentObservation> result = await provider.GetDeploymentsAsync(
            new DeploymentRequest(TenantId.From("acme"), EnvironmentId.From("reference"), new TimeRange(From, From.AddMinutes(1))), CancellationToken.None);

        result.Observations.Select(observation => observation.ObservationId.Value).ShouldBe([
            "deploy-controller:deployment:d1:started",
            "deploy-controller:deployment:d1:completed",
            "deploy-controller:deployment:c1:completed"]);
        result.Observations[0].Status.ShouldBe("in-progress");
        result.Observations[0].CompletedAt.ShouldBeNull();
        result.Observations[1].PreviousVersion.ShouldBe("v2.4.0");
        result.Observations[2].ChangeKind.ShouldBe("config-change");
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
