using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Hosting;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;

namespace Sentinel.AgentWorker.Tests;

/// <summary>
/// AgentWorker's HTTP surface as the Gateway and operators see it: routes, status codes and bodies of
/// the agent executions, investigation replay and the model configuration (Phases 07 and 08).
/// </summary>
public sealed class AgentWorkerEndpointTests
{
    private static readonly DateTimeOffset Now = AgentWorkerHost.Now;

    [Fact]
    public async Task An_incidents_executions_are_listed_and_read_with_their_tool_calls()
    {
        await using var host = new AgentWorkerHost(AgentWorkerHost.Testing);
        AgentExecutionRecord execution = Execution(IncidentId.New());
        await host.Executions.SaveAsync(execution, CancellationToken.None);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage listed = await client.GetAsync(new Uri($"/v1/executions?incidentId={execution.IncidentId}", UriKind.Relative));
        using HttpResponseMessage read = await client.GetAsync(new Uri($"/v1/executions/{execution.AgentExecutionId}", UriKind.Relative));
        using HttpResponseMessage other = await client.GetAsync(new Uri($"/v1/executions?incidentId={Guid.NewGuid()}", UriKind.Relative));

        listed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(listed)).EnumerateArray().Single().GetProperty("agentExecutionId").GetString().ShouldBe(execution.AgentExecutionId.ToString());
        (await BodyAsync(other)).GetArrayLength().ShouldBe(0);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement body = await BodyAsync(read);
        Names(body).ShouldBe([
            "agentExecutionId", "tenantId", "environmentId", "incidentId", "investigationId", "investigator", "shadow", "modelProvider",
            "model", "modelVersion", "promptVersion", "result", "abstentionReason", "toolCalls", "modelCalls", "inputTokens", "outputTokens",
            "schemaFailures", "findings", "startedAt", "completedAt"]);
        body.GetProperty("result").GetString().ShouldBe("abstained");
        body.GetProperty("inputTokens").GetInt32().ShouldBe(1200);
        JsonElement toolCall = body.GetProperty("toolCalls").EnumerateArray().Single();
        Names(toolCall).ShouldBe(["toolCallId", "toolName", "arguments", "result", "observationIds", "partial", "startedAt", "duration"]);
        toolCall.GetProperty("duration").GetString().ShouldBe("00:00:00.2500000");
    }

    [Fact]
    public async Task An_unknown_execution_is_not_found()
    {
        await using var host = new AgentWorkerHost(AgentWorkerHost.Testing);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage read = await client.GetAsync(new Uri($"/v1/executions/{Guid.NewGuid()}", UriKind.Relative));
        using HttpResponseMessage replay = await client.PostAsJsonAsync($"/v1/executions/{Guid.NewGuid()}/replays", new JsonObject());

        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        replay.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_execution_recorded_without_its_inputs_replays_as_not_reproducible()
    {
        await using var host = new AgentWorkerHost(AgentWorkerHost.Testing);
        AgentExecutionRecord execution = Execution(IncidentId.New());
        await host.Executions.SaveAsync(execution, CancellationToken.None);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync($"/v1/executions/{execution.AgentExecutionId}/replays", new JsonObject { ["level"] = "investigation" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement replay = await BodyAsync(response);
        replay.GetProperty("agentExecutionId").GetString().ShouldBe(execution.AgentExecutionId.ToString());
        replay.GetProperty("level").GetString().ShouldBe("investigation");
        replay.GetProperty("deterministic").GetBoolean().ShouldBeFalse();
        replay.GetProperty("inputsReproduced").GetBoolean().ShouldBeFalse();
        replay.GetProperty("explanation").GetString()!.ShouldStartWith("The execution was recorded before its inputs were captured");
        host.Executions.Replays.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_replay_at_another_services_level_or_an_unknown_prompt_is_a_bad_request()
    {
        await using var host = new AgentWorkerHost(AgentWorkerHost.Testing);
        AgentExecutionRecord execution = Execution(IncidentId.New());
        await host.Executions.SaveAsync(execution, CancellationToken.None);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage domain = await client.PostAsJsonAsync($"/v1/executions/{execution.AgentExecutionId}/replays", new JsonObject { ["level"] = "domain" });
        using HttpResponseMessage prompt = await client.PostAsJsonAsync($"/v1/executions/{execution.AgentExecutionId}/replays", new JsonObject { ["level"] = "modelReevaluation", ["promptVersion"] = "no-such-prompt" });

        JsonElement wrongLevel = await ShouldBeProblemAsync(domain, HttpStatusCode.BadRequest, "Bad Request");
        wrongLevel.GetProperty("detail").GetString().ShouldBe("AgentWorker replays levels Investigation and ModelReevaluation; Domain belongs to another service. (Parameter 'request')");
        wrongLevel.TryGetProperty("code", out _).ShouldBeFalse();
        JsonElement unknownPrompt = await ShouldBeProblemAsync(prompt, HttpStatusCode.BadRequest, "Bad Request");
        unknownPrompt.GetProperty("detail").GetString()!.ShouldStartWith("Prompt version 'no-such-prompt' is not one of ");
        host.Executions.Replays.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_model_configuration_is_seeded_from_the_deployment_and_published_over_it()
    {
        await using var host = new AgentWorkerHost(AgentWorkerHost.Testing);
        using HttpClient client = host.CreateClient();

        JsonObject seeded = (await client.GetFromJsonAsync<JsonObject>(new Uri("/v1/configuration", UriKind.Relative)))!;
        seeded["version"]!["version"]!.GetValue<string>().ShouldBe("deployment");
        seeded["published"]!.GetValue<bool>().ShouldBeFalse();
        JsonObject document = seeded["document"]!.AsObject();
        document["version"] = "models-2026.10.29.1";
        document["shadowBaseline"] = false;

        using HttpResponseMessage published = await client.PutAsJsonAsync("/admin/configuration", Publication(document, "deployment"));
        using HttpResponseMessage stale = await client.PutAsJsonAsync("/admin/configuration", Publication(document, "deployment", "models-2026.10.29.2"));
        document["defaultProvider"] = "meta";
        using HttpResponseMessage unavailable = await client.PutAsJsonAsync("/admin/configuration", Publication(document, "models-2026.10.29.1", "models-2026.10.29.3"));

        published.StatusCode.ShouldBe(HttpStatusCode.OK);
        JsonElement version = await BodyAsync(published);
        version.GetProperty("version").GetString().ShouldBe("models-2026.10.29.1");
        version.GetProperty("author").GetString().ShouldBe("operator@example.test");
        version.GetProperty("basedOn").GetString().ShouldBe("deployment");
        JsonElement conflict = await ShouldBeProblemAsync(stale, HttpStatusCode.Conflict, "Configuration.VersionConflict");
        conflict.TryGetProperty("code", out _).ShouldBeFalse();
        JsonElement invalid = await ShouldBeProblemAsync(unavailable, HttpStatusCode.BadRequest, "Configuration.Invalid");
        invalid.GetProperty("detail").GetString()!.ShouldContain("Provider 'meta' is not configured in this deployment; available: test-double.");

        JsonObject current = (await client.GetFromJsonAsync<JsonObject>(new Uri("/v1/configuration", UriKind.Relative)))!;
        current["published"]!.GetValue<bool>().ShouldBeTrue();
        current["document"]!["shadowBaseline"]!.GetValue<bool>().ShouldBeFalse();
        using HttpResponseMessage history = await client.GetAsync(new Uri("/v1/configuration/history", UriKind.Relative));
        history.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyAsync(history)).EnumerateArray().Select(entry => entry.GetProperty("version").GetString()).ShouldBe(["models-2026.10.29.1", "deployment"]);
    }

    [Fact]
    public async Task Publishing_the_model_configuration_is_absent_where_operator_endpoints_are_disabled()
    {
        await using var host = new AgentWorkerHost(Environments.Production, operatorEndpoints: false);
        using HttpClient client = host.CreateClient();

        using HttpResponseMessage publish = await client.PutAsJsonAsync("/admin/configuration", new JsonObject());
        using HttpResponseMessage read = await client.GetAsync(new Uri("/v1/configuration", UriKind.Relative));

        publish.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static AgentExecutionRecord Execution(IncidentId incidentId) => new()
    {
        AgentExecutionId = AgentExecutionId.New(),
        TenantId = TenantId.From("acme"),
        EnvironmentId = EnvironmentId.From("reference"),
        IncidentId = incidentId,
        InvestigationId = InvestigationId.New(),
        Investigator = "model",
        Shadow = false,
        ModelProvider = "sentinel-test-double",
        Model = "deterministic-analyst",
        ModelVersion = "1",
        PromptVersion = "investigator-v3",
        Result = ExecutionResult.Abstained,
        AbstentionReason = "The model token budget was exhausted.",
        ToolCalls =
        [
            new ToolCallRecord
            {
                ToolCallId = "call-1",
                ToolName = "metric_range",
                Arguments = """{"metric":"slo_burn_rate"}""",
                Result = "[]",
                ObservationIds = [ObservationId.From("prometheus:slo_burn_rate:checkout-service:1")],
                StartedAt = Now.AddSeconds(-30),
                Duration = TimeSpan.FromMilliseconds(250)
            }
        ],
        ModelCalls = 2,
        InputTokens = 1200,
        OutputTokens = 300,
        Findings = "{}",
        StartedAt = Now.AddSeconds(-31),
        CompletedAt = Now.AddSeconds(-20)
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

    private static async Task<JsonElement> ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string title)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        JsonElement problem = await BodyAsync(response);
        problem.GetProperty("status").GetInt32().ShouldBe((int)status);
        problem.GetProperty("title").GetString().ShouldBe(title);
        return problem;
    }
}
