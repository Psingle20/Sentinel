using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sentinel.AgentWorker.Application.Abstractions.Providers;

namespace Sentinel.AgentWorker.Infrastructure.Models;

/// <summary>
/// The Meta Model API adapter (ADR-015) over the OpenAI-compatible chat completions protocol, one instance per
/// <see cref="MetaModelProfile"/>. It offers the tools and the findings' JSON Schema in the same request, so a final
/// answer is shaped by the API; reasoning is requested at the profile's effort and given its own share of the output
/// limit, which it counts against. Prices come from the profile; the response's usage reports cached input and reasoning.
/// </summary>
public sealed class MetaModelProvider(HttpClient http, string name, MetaModelProfile profile, MetaModelOptions connection, ILogger<MetaModelProvider> logger) : IModelProvider
{
    /// <summary>The default profile's name, and the prefix every Meta profile's name starts with.</summary>
    public const string ProviderName = "meta";

    public string Provider => name;

    public string Model => profile.Model;

    public string? ModelVersion => profile.ModelVersion;

    public ModelPrice Price => profile.Price;

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new JsonObject
        {
            ["model"] = Model,
            ["temperature"] = 0,
            ["max_tokens"] = request.MaxOutputTokens + profile.ReasoningTokens,
            ["messages"] = new JsonArray([.. request.Messages.Select(Message)]),
            ["tools"] = new JsonArray([.. request.Tools.Select(tool => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.ParametersSchema)
                }
            })]),
            ["tool_choice"] = "auto"
        };

        if (profile.ReasoningEffort is { } effort)
        {
            body["reasoning_effort"] = effort;
        }

        if (request.OutputSchema is { } schema)
        {
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = SchemaName(request.OutputSchemaName), ["schema"] = JsonNode.Parse(schema) }
            };
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(connection.RequestTimeout);
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, connection.ChatCompletionsUri)
            {
                Content = JsonContent.Create(body)
            };
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", connection.ApiKey);
            using HttpResponseMessage response = await http.SendAsync(message, timeout.Token);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return ModelResponse.Failed(ModelFinish.RateLimited, "The model API is rate limiting requests.");
            }

            if (!response.IsSuccessStatusCode)
            {
                // The error body says why a request was refused, for example an unsupported parameter; only its start is logged.
                string detail = await response.Content.ReadAsStringAsync(timeout.Token);
                logger.LogWarning("Model API {Model} returned {Status}: {Detail}", Model, (int)response.StatusCode, detail.Length > 500 ? detail[..500] : detail);
                return ModelResponse.Failed(ModelFinish.Error, $"The model API returned {(int)response.StatusCode}.");
            }

            JsonNode? root = await response.Content.ReadFromJsonAsync<JsonNode>(timeout.Token);
            return Translate(root);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return ModelResponse.Failed(ModelFinish.Timeout, $"No response within {connection.RequestTimeout}.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            logger.LogWarning(exception, "Model call failed");
            return ModelResponse.Failed(ModelFinish.Error, exception.Message);
        }
    }

    private static JsonNode Message(ModelMessage message)
    {
        var node = new JsonObject
        {
            ["role"] = message.Role switch
            {
                ModelRole.System => "system",
                ModelRole.User => "user",
                ModelRole.Assistant => "assistant",
                _ => "tool"
            },
            ["content"] = message.Content
        };

        if (message.ToolCallId is not null)
        {
            node["tool_call_id"] = message.ToolCallId;
        }

        if (message.ToolCalls is { Count: > 0 } calls)
        {
            node["tool_calls"] = new JsonArray([.. calls.Select(call => (JsonNode)new JsonObject
            {
                ["id"] = call.Id,
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.Arguments }
            })]);
        }

        return node;
    }

    private static ModelResponse Translate(JsonNode? root)
    {
        JsonNode? choice = root?["choices"]?[0];
        JsonNode? message = choice?["message"];
        JsonNode? reported = root?["usage"];
        var usage = new ModelUsage(
            reported?["prompt_tokens"]?.GetValue<int>() ?? 0,
            reported?["completion_tokens"]?.GetValue<int>() ?? 0,
            reported?["prompt_tokens_details"]?["cached_tokens"]?.GetValue<int>() ?? 0,
            reported?["completion_tokens_details"]?["reasoning_tokens"]?.GetValue<int>() ?? 0);

        if (message is null)
        {
            return ModelResponse.Failed(ModelFinish.Error, "The response has no message.");
        }

        string? finish = choice?["finish_reason"]?.GetValue<string>();
        if (finish == "content_filter")
        {
            return new ModelResponse(ModelFinish.Refused, [], null, usage, "The model declined to answer.");
        }

        if (finish == "length")
        {
            return new ModelResponse(ModelFinish.Truncated, [], null, usage, $"The answer reached the output limit after {usage.ReasoningTokens} reasoning tokens.");
        }

        if (message["tool_calls"] is JsonArray toolCalls && toolCalls.Count > 0)
        {
            return new ModelResponse(
                ModelFinish.ToolCalls,
                [.. toolCalls.Select(call => new ModelToolCall(
                    call!["id"]!.GetValue<string>(),
                    call["function"]!["name"]!.GetValue<string>(),
                    call["function"]!["arguments"]?.GetValue<string>() ?? "{}"))],
                null,
                usage);
        }

        string content = message["content"]?.GetValue<string>() ?? string.Empty;
        return new ModelResponse(ModelFinish.Final, [], StripFences(content), usage);
    }

    // The API accepts letters, digits, underscores and hyphens in a schema's name.
    private static string SchemaName(string schemaName) =>
        new([.. schemaName.Select(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' ? character : '_')]);

    // Some models wrap JSON in a Markdown code fence; the document inside is what is validated.
    private static string StripFences(string content)
    {
        string trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        int firstLine = trimmed.IndexOf('\n', StringComparison.Ordinal);
        int closing = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLine > 0 && closing > firstLine ? trimmed[(firstLine + 1)..closing].Trim() : trimmed;
    }
}
