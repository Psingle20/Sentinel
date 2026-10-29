namespace Sentinel.AgentWorker.Infrastructure.Models;

/// <summary>
/// Configured as <c>Model:Meta</c>: the connection, shared by every profile, and the profiles themselves. The endpoint
/// and profiles are deployment configuration; the API key comes from local secret configuration or a secret manager and
/// is never committed. A configuration without profiles that names <see cref="Model"/> is the single profile
/// <c>meta</c>, as before profiles existed.
/// </summary>
public sealed class MetaModelOptions
{
    /// <summary>The full URL of the OpenAI-compatible chat completions endpoint, for example <c>https://api.meta.ai/v1/chat/completions</c>.</summary>
    public Uri? ChatCompletionsUri { get; set; }

    public string? ApiKey { get; set; }

    /// <summary>The longest one model call may take; reasoning at high effort takes longer than a plain answer.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(180);

    public IDictionary<string, MetaModelProfile> Profiles { get; } = new Dictionary<string, MetaModelProfile>(StringComparer.Ordinal);

    /// <summary>The single model of a configuration made before profiles; ignored when <see cref="Profiles"/> is set.</summary>
    public string Model { get; set; } = string.Empty;

    public string? ModelVersion { get; set; }

    /// <summary>Every profile by provider name, including the single model of a configuration made before profiles.</summary>
    public IReadOnlyDictionary<string, MetaModelProfile> EffectiveProfiles() =>
        Profiles.Count > 0 || string.IsNullOrWhiteSpace(Model)
            ? new Dictionary<string, MetaModelProfile>(Profiles, StringComparer.Ordinal)
            : new Dictionary<string, MetaModelProfile>(StringComparer.Ordinal) { [MetaModelProvider.ProviderName] = new() { Model = Model, ModelVersion = ModelVersion } };

    public bool IsValid() =>
        ChatCompletionsUri is { IsAbsoluteUri: true }
        && !string.IsNullOrWhiteSpace(ApiKey)
        && RequestTimeout > TimeSpan.Zero
        && EffectiveProfiles() is { Count: > 0 } profiles
        && profiles.All(profile => profile.Key.StartsWith(MetaModelProvider.ProviderName, StringComparison.Ordinal) && profile.Value.IsValid());
}
