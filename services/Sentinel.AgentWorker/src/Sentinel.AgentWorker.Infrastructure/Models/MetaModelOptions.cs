using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
