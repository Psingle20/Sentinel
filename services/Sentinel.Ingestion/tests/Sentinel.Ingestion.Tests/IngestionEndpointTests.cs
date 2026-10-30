using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Sentinel.Contracts;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Domain.Sources;
using Shouldly;

namespace Sentinel.Ingestion.Tests;

/// <summary>
/// Ingestion's HTTP surface as its peers and operators see it: routes, status codes and bodies of the
/// query contract, knowledge search, source health and the source configuration (ADR-007, Phase 08).
/// </summary>
public sealed class IngestionEndpointTests
{
    private static readonly DateTimeOffset Now = IngestionHost.Now;

    [Fact]
    public async Task An_observation_query_answers_with_what_the_answer_can_be_trusted_for()
    {
        await using var host = new IngestionHost(Environments.Development);
        host.Sources.Add(Source("prometheus:slo_burn_rate", Now.AddSeconds(-10)));
        host.Observations.Add(Sample(Now.AddSeconds(-20)));
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/v1/observations/query", Query(), SentinelJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement body = await BodyAsync(response);
        Names(body).ShouldBe(["observations", "metadata"]);
        body.GetProperty("observations").GetArrayLength().ShouldBe(1);
        JsonElement metadata = body.GetProperty("metadata");
        metadata.GetProperty("observedThrough").GetDateTimeOffset().ShouldBe(Now.AddSeconds(-10));
        metadata.GetProperty("freshness").GetString().ShouldBe("00:00:10");
        metadata.GetProperty("completeness").GetString().ShouldBe("complete");
        metadata.GetProperty("truncated").GetBoolean().ShouldBeFalse();
        JsonElement provider = metadata.GetProperty("providerHealth").EnumerateArray().Single();
        provider.GetProperty("provider").GetString().ShouldBe("prometheus");
        provider.GetProperty("status").GetString().ShouldBe("healthy");
        metadata.GetProperty("redactionPolicyVersion").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task A_query_outside_the_ingested_scope_or_with_an_inverted_range_is_refused()
    {
        await using var host = new IngestionHost(Environments.Development);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage unknown = await client.PostAsJsonAsync("/v1/observations/query", Query() with { TenantId = TenantId.From("globex") }, SentinelJson.Options);
        using HttpResponseMessage inverted = await client.PostAsJsonAsync("/v1/observations/query", Query() with { Range = new TimeRange(Now, Now.AddMinutes(-1)) }, SentinelJson.Options);

        await ShouldBeProblemAsync(unknown, HttpStatusCode.NotFound, "Observations.UnknownScope");
        await ShouldBeProblemAsync(inverted, HttpStatusCode.UnprocessableEntity, "Observations.InvalidRange");
    }

    [Fact]
    public async Task A_query_beyond_the_bulkhead_is_refused_at_once_with_service_unavailable()
    {
        await using var host = new IngestionHost(Environments.Development);
        using HttpClient client = host.CreateClient();
        host.Observations.Hold();

        Task<HttpResponseMessage> first = client.PostAsJsonAsync("/v1/observations/query", Query(), SentinelJson.Options);
        await host.Observations.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        using HttpResponseMessage refused = await client.PostAsJsonAsync("/v1/knowledge/search", Search(), SentinelJson.Options);
        host.Observations.Release();
        using HttpResponseMessage answered = await first;

        JsonElement problem = await ShouldBeProblemAsync(refused, HttpStatusCode.ServiceUnavailable, "Ingestion.QueryBulkheadFull");
        problem.GetProperty("detail").GetString().ShouldBe("Ingestion is serving its maximum number of queries; retry shortly.");
        answered.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_knowledge_search_returns_the_best_matching_passage_as_data()
    {
        await using var host = new IngestionHost(Environments.Development);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/v1/knowledge/search", Search(), SentinelJson.Options);
        using HttpResponseMessage unknown = await client.PostAsJsonAsync("/v1/knowledge/search", Search() with { TenantId = TenantId.From("globex") }, SentinelJson.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement body = await BodyAsync(response);
        Names(body).ShouldBe(["documents", "redactionPolicyVersion"]);
        JsonElement document = body.GetProperty("documents").EnumerateArray().Single();
        document.GetProperty("documentId").GetString().ShouldBe("runbooks:redis-connection-exhaustion");
        document.GetProperty("title").GetString().ShouldBe("Redis connection exhaustion");
        document.GetProperty("excerpt").GetString().ShouldBe("Roll back the deployment that raised Redis connections.");
        document.GetProperty("score").GetDouble().ShouldBe(5d / 12, 0.0001);
        await ShouldBeProblemAsync(unknown, HttpStatusCode.NotFound, "Observations.UnknownScope");
    }

    [Fact]
    public async Task Sources_report_their_health_and_watermark_for_operators()
    {
        await using var host = new IngestionHost(Environments.Development);
        host.Sources.Add(Source("prometheus:redis_connections", Now.AddSeconds(-40)) with { Health = SourceHealth.Degraded, LastError = "connection refused", ConsecutiveFailures = 1 });
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/v1/sources", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement source = (await BodyAsync(response)).EnumerateArray().Single();
        Names(source).ShouldBe(["sourceId", "provider", "watermark", "health", "lastError", "consecutiveFailures", "healthy", "lastPulledAt"], "nextAttemptAt appears only once a failing source backs off");
        source.GetProperty("sourceId").GetString().ShouldBe("prometheus:redis_connections");
        source.GetProperty("watermark").GetDateTimeOffset().ShouldBe(Now.AddSeconds(-40));
        source.GetProperty("health").GetString().ShouldBe("Degraded");
        source.GetProperty("lastError").GetString().ShouldBe("connection refused");
        source.GetProperty("consecutiveFailures").GetInt32().ShouldBe(1);
        source.GetProperty("healthy").GetBoolean().ShouldBeFalse();
        source.GetProperty("lastPulledAt").ValueKind.ShouldBe(JsonValueKind.String);
    }

    [Fact]
    public async Task The_source_configuration_is_seeded_from_the_deployment_and_published_over_it()
    {
        await using var host = new IngestionHost(Environments.Development);
        using HttpClient client = host.CreateClient();

        JsonObject seeded = (await client.GetFromJsonAsync<JsonObject>(new Uri("/v1/configuration", UriKind.Relative)))!;
        seeded["version"]!["version"]!.GetValue<string>().ShouldBe("deployment");
        seeded["published"]!.GetValue<bool>().ShouldBeFalse();
        JsonObject document = seeded["document"]!.AsObject();
        document["version"] = "sources-2026.10.29.1";
        document["topologyEnabled"] = false;

        using HttpResponseMessage published = await client.PutAsJsonAsync("/admin/configuration", Publication(document, "deployment"));
        using HttpResponseMessage stale = await client.PutAsJsonAsync("/admin/configuration", Publication(document, "deployment", "sources-2026.10.29.2"));
        document["metricSeries"] = new JsonArray();
        using HttpResponseMessage invalid = await client.PutAsJsonAsync("/admin/configuration", Publication(document, "sources-2026.10.29.1", "sources-2026.10.29.3"));

        published.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement version = await BodyAsync(published);
        version.GetProperty("version").GetString().ShouldBe("sources-2026.10.29.1");
        version.GetProperty("author").GetString().ShouldBe("operator@example.test");
        version.GetProperty("basedOn").GetString().ShouldBe("deployment");
        await ShouldBeProblemAsync(stale, HttpStatusCode.Conflict, "Configuration.VersionConflict");
        await ShouldBeProblemAsync(invalid, HttpStatusCode.UnprocessableEntity, "Configuration.Invalid");

        JsonObject current = (await client.GetFromJsonAsync<JsonObject>(new Uri("/v1/configuration", UriKind.Relative)))!;
        current["published"]!.GetValue<bool>().ShouldBeTrue();
        current["document"]!["topologyEnabled"]!.GetValue<bool>().ShouldBeFalse();
        using HttpResponseMessage history = await client.GetAsync(new Uri("/v1/configuration/history", UriKind.Relative));
        history.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(history)).EnumerateArray().Select(entry => entry.GetProperty("version").GetString()).ShouldBe(["sources-2026.10.29.1", "deployment"]);
    }

    [Fact]
    public async Task Publishing_the_source_configuration_is_absent_where_operator_endpoints_are_disabled()
    {
        await using var host = new IngestionHost(Environments.Production);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage publish = await client.PutAsJsonAsync("/admin/configuration", new JsonObject());
        using HttpResponseMessage read = await client.GetAsync(new Uri("/v1/configuration", UriKind.Relative));

        publish.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static ObservationQuery Query() => new()
    {
        TenantId = TenantId.From("acme"),
        EnvironmentId = EnvironmentId.From("reference"),
        Kind = ObservationKind.Metric,
        Range = new TimeRange(Now.AddMinutes(-5), Now),
        Filters = new Dictionary<string, string> { ["metricName"] = "slo_burn_rate" }
    };

    private static KnowledgeQuery Search() => new()
    {
        TenantId = TenantId.From("acme"),
        EnvironmentId = EnvironmentId.From("reference"),
        Text = "rollback redis connections deployment"
    };

    private static SourceState Source(string sourceId, DateTimeOffset watermark) => new()
    {
        SourceId = sourceId,
        Provider = "prometheus",
        Checkpoint = watermark,
        Watermark = watermark,
        Health = SourceHealth.Healthy,
        UpdatedAt = Now
    };

    private static MetricObservation Sample(DateTimeOffset eventTime) => new()
    {
        ObservationId = ObservationId.From("prometheus:slo_burn_rate:checkout-service:1"),
        TenantId = TenantId.From("acme"),
        EnvironmentId = EnvironmentId.From("reference"),
        ServiceId = ServiceId.From("checkout-service"),
        SourceProvider = "prometheus",
        SourceResource = "slo:burn",
        EventTime = eventTime,
        IngestTime = Now,
        MetricName = "slo_burn_rate",
        Value = 0.1
    };

    private static JsonObject Publication(JsonObject document, string expectedVersion, string? version = null)
    {
        JsonNode copy = document.DeepClone();
        if (version is not null)
        {
            copy["version"] = version;
        }

        return new JsonObject { ["document"] = copy, ["expectedVersion"] = expectedVersion, ["author"] = "operator@example.test" };
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static string[] Names(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    private static async Task<JsonElement> ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        JsonElement problem = await BodyAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe((int)status);
        problem.GetProperty("title").GetString().ShouldBe(code);
        problem.GetProperty("code").GetString().ShouldBe(code);
        return problem;
    }
}
