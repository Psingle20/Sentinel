using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Models;

namespace Sentinel.AgentWorker.Infrastructure.Models;

/// <summary>
/// Configured as <c>Model:Meta</c>. The endpoint and model name come from configuration and the
/// API key from local secret configuration or a secret manager; none is committed.
/// </summary>
public sealed class MetaModelOptions
{
    /// <summary>The full URL of an OpenAI-compatible chat completions endpoint, for example the Meta Llama API's compatibility endpoint.</summary>
    public Uri? ChatCompletionsUri { get; set; }

    public string Model { get; set; } = string.Empty;

    public string? ModelVersion { get; set; }

    public string? ApiKey { get; set; }

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public bool IsValid() => ChatCompletionsUri is { IsAbsoluteUri: true } && !string.IsNullOrWhiteSpace(Model) && !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// The Meta Model API adapter (ADR-015) over the OpenAI-compatible chat completions protocol with
/// function tools. It is selected with <c>Model:Provider = meta</c>; its wire details are confirmed
/// against the live API once the Muse Spark details are available.
/// </summary>
public sealed class MetaModelProvider(HttpClient http, IOptions<MetaModelOptions> options, ILogger<MetaModelProvider> logger) : IModelProvider
{
    public const string ProviderName = "meta";

    public string Provider => ProviderName;

    public string Model => options.Value.Model;

    public string? ModelVersion => options.Value.ModelVersion;

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new JsonObject
        {
            ["model"] = Model,
            ["temperature"] = 0,
            ["max_tokens"] = request.MaxOutputTokens,
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

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.RequestTimeout);
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, options.Value.ChatCompletionsUri)
            {
                Content = JsonContent.Create(body)
            };
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
            using HttpResponseMessage response = await http.SendAsync(message, timeout.Token);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return ModelResponse.Failed(ModelFinish.RateLimited, "The model API is rate limiting requests.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return ModelResponse.Failed(ModelFinish.Error, $"The model API returned {(int)response.StatusCode}.");
            }

            JsonNode? root = await response.Content.ReadFromJsonAsync<JsonNode>(timeout.Token);
            return Translate(root);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return ModelResponse.Failed(ModelFinish.Timeout, $"No response within {options.Value.RequestTimeout}.");
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
        var usage = new ModelUsage(
            root?["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0,
            root?["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0);

        if (message is null)
        {
            return ModelResponse.Failed(ModelFinish.Error, "The response has no message.");
        }

        if (choice?["finish_reason"]?.GetValue<string>() == "content_filter")
        {
            return new ModelResponse(ModelFinish.Refused, [], null, usage, "The model declined to answer.");
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
