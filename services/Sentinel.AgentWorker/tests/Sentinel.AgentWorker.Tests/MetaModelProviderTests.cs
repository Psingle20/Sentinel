using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Infrastructure.Models;
using Shouldly;

namespace Sentinel.AgentWorker.Tests;

/// <summary>The Meta adapter's wire format, against a recording handler: no request leaves the test.</summary>
public sealed class MetaModelProviderTests
{
    private static readonly MetaModelOptions Connection = CreateConnection();

    [Fact]
    public async Task A_request_carries_the_profiles_model_and_effort_reasoning_headroom_the_tools_and_the_findings_schema()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"choices":[{"finish_reason":"stop","message":{"content":"{}"}}]}""");
        MetaModelProvider model = Provider(handler, new MetaModelProfile { Model = "muse-spark-1.3", ReasoningEffort = "medium", ReasoningTokens = 8_000 });

        await model.CompleteAsync(Request(), CancellationToken.None);

        JsonNode body = handler.Body.ShouldNotBeNull();
        body["model"]!.GetValue<string>().ShouldBe("muse-spark-1.3");
        body["reasoning_effort"]!.GetValue<string>().ShouldBe("medium");
        body["max_tokens"]!.GetValue<int>().ShouldBe(12_000);
        body["tools"]!.AsArray().Single()!["function"]!["name"]!.GetValue<string>().ShouldBe("metric_range");
        body["response_format"]!["type"]!.GetValue<string>().ShouldBe("json_schema");
        body["response_format"]!["json_schema"]!["name"]!.GetValue<string>().ShouldBe("sentinel_investigation-findings_v1");
        body["response_format"]!["json_schema"]!["schema"]!["required"]!.AsArray().Select(item => item!.GetValue<string>()).ShouldContain("hypotheses");
        handler.Authorization.ShouldBe("Bearer provider-test-not-a-token");
    }

    [Fact]
    public async Task Usage_reports_cached_input_and_reasoning_and_the_price_comes_from_the_profile()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """
            {"choices":[{"finish_reason":"tool_calls","message":{"tool_calls":[{"id":"call_1","type":"function","function":{"name":"metric_range","arguments":"{}"}}]}}],
             "usage":{"prompt_tokens":1000,"completion_tokens":300,"prompt_tokens_details":{"cached_tokens":600},"completion_tokens_details":{"reasoning_tokens":250}}}
            """);
        MetaModelProvider model = Provider(handler, new MetaModelProfile { Model = "muse-spark-1.3", InputPricePerMillionTokens = 1.25, CachedInputPricePerMillionTokens = 0.15, OutputPricePerMillionTokens = 4.25 });

        ModelResponse response = await model.CompleteAsync(Request(), CancellationToken.None);

        response.Finish.ShouldBe(ModelFinish.ToolCalls);
        response.Usage.ShouldBe(new ModelUsage(1000, 300, 600, 250));
        model.Price.Estimate(response.Usage).ShouldBe((400 * 1.25 + 600 * 0.15 + 300 * 4.25) / 1_000_000, tolerance: 1e-12);
    }

    [Fact]
    public async Task An_answer_cut_off_at_the_output_limit_is_truncated_not_final()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """
            {"choices":[{"finish_reason":"length","message":{"content":"{\"abstain\": fal"}}],"usage":{"prompt_tokens":10,"completion_tokens":12000,"completion_tokens_details":{"reasoning_tokens":11900}}}
            """);

        ModelResponse response = await Provider(handler, new MetaModelProfile { Model = "muse-spark-1.3" }).CompleteAsync(Request(), CancellationToken.None);

        response.Finish.ShouldBe(ModelFinish.Truncated);
        response.Content.ShouldBeNull();
        response.Error.ShouldNotBeNull().ShouldContain("11900 reasoning tokens");
    }

    [Fact]
    public async Task A_refused_request_is_an_error_naming_the_status()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{"error":{"message":"unsupported parameter"}}""");

        ModelResponse response = await Provider(handler, new MetaModelProfile { Model = "muse-spark-1.3" }).CompleteAsync(Request(), CancellationToken.None);

        response.Finish.ShouldBe(ModelFinish.Error);
        response.Error.ShouldBe("The model API returned 400.");
    }

    [Fact]
    public async Task A_rate_limited_request_is_reported_as_rate_limited_and_not_retried()
    {
        var handler = new RecordingHandler(HttpStatusCode.TooManyRequests, """{"error":{"message":"rate limit exceeded"}}""");

        ModelResponse response = await Provider(handler, new MetaModelProfile { Model = "muse-spark-1.3" }).CompleteAsync(Request(), CancellationToken.None);

        response.Finish.ShouldBe(ModelFinish.RateLimited);
        response.Content.ShouldBeNull();
        response.Error.ShouldBe("The model API is rate limiting requests.");
        handler.Requests.ShouldBe(1, "the adapter leaves retrying to the harness");
    }

    [Fact]
    public void A_configuration_made_before_profiles_is_the_single_profile_meta()
    {
        var legacy = new MetaModelOptions { ChatCompletionsUri = Connection.ChatCompletionsUri, ApiKey = Connection.ApiKey, Model = "muse-spark-1.3" };

        legacy.EffectiveProfiles().Keys.ShouldBe([MetaModelProvider.ProviderName]);
        legacy.IsValid().ShouldBeTrue();
    }

    [Fact]
    public void Profiles_must_be_named_after_meta_and_name_a_known_effort()
    {
        var options = new MetaModelOptions { ChatCompletionsUri = Connection.ChatCompletionsUri, ApiKey = Connection.ApiKey };
        options.Profiles["deep"] = new MetaModelProfile { Model = "muse-spark-1.3" };
        options.IsValid().ShouldBeFalse("a profile's name is the provider a role routes to, and Meta's start with meta");

        options.Profiles.Clear();
        options.Profiles["meta-deep"] = new MetaModelProfile { Model = "muse-spark-1.3", ReasoningEffort = "extreme" };
        options.IsValid().ShouldBeFalse();

        options.Profiles["meta-deep"].ReasoningEffort = "high";
        options.IsValid().ShouldBeTrue();
    }

    private static MetaModelProvider Provider(RecordingHandler handler, MetaModelProfile profile) =>
        new(new HttpClient(handler), MetaModelProvider.ProviderName, profile, Connection, NullLogger<MetaModelProvider>.Instance);

    private static ModelRequest Request() => new(
        "investigator-v3/metrics",
        [ModelMessage.System("You investigate."), ModelMessage.User("Investigate.")],
        [new ModelToolDefinition("metric_range", "Reads a metric.", """{"type":"object","properties":{}}""")],
        FindingsValidator.OutputSchemaName,
        4_000,
        FindingsValidator.OutputJsonSchema);

    private static MetaModelOptions CreateConnection() => new()
    {
        ChatCompletionsUri = new Uri("https://model-api.example.test/v1/chat/completions"),
        ApiKey = "provider-test-not-a-token"
    };

    private sealed class RecordingHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public JsonNode? Body { get; private set; }

        public string? Authorization { get; private set; }

        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Authorization = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
