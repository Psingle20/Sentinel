using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.Contracts.Investigations;

namespace Sentinel.AgentWorker.Application.Models;

/// <summary>Configured as <c>Model</c>: the default provider and per-role overrides (Phase 04 model routing).</summary>
public sealed class ModelRoutingOptions
{
    public const string Section = "Model";

    /// <summary>The name used for the deterministic test double in configuration.</summary>
    public const string TestDouble = "test-double";

    /// <summary><c>test-double</c> or <c>meta</c>.</summary>
    public string Provider { get; set; } = TestDouble;

    /// <summary>A provider per investigator role, overriding <see cref="Provider"/>.</summary>
    public IDictionary<string, string> Routing { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public string ProviderFor(string role) => Routing.TryGetValue(role, out string? routed) ? routed : Provider;
}

/// <summary>
/// AgentWorker's control-plane document (Phase 08 model selection): which provider serves each role,
/// the investigator prompt version, the model's budget per investigation, whether the deploy-blame
/// baseline runs in shadow, and the token prices used for cost estimates. Endpoints and credentials
/// are deployment configuration and never part of it; a provider can be selected only if it is
/// configured, so the control plane cannot route an investigation to a provider without credentials.
/// </summary>
public sealed record ModelConfigurationDocument : IVersionedDocument
{
    public required string Version { get; init; }

    public string DefaultProvider { get; init; } = ModelRoutingOptions.TestDouble;

    public IReadOnlyDictionary<string, string> Routing { get; init; } = new Dictionary<string, string>();

    public string PromptVersion { get; init; } = InvestigatorPrompt.Version;

    public int MaxModelTurns { get; init; } = 6;

    public int MaxOutputTokens { get; init; } = 4_000;

    public bool ShadowBaseline { get; init; } = true;

    public double InputPricePerMillionTokens { get; init; }

    public double OutputPricePerMillionTokens { get; init; }
}

/// <summary>Moves AgentWorker's model options to and from the control-plane document.</summary>
public static class ModelConfigurationMapping
{
    public const string DeploymentVersion = "deployment";

    public static ModelConfigurationDocument From(ModelRoutingOptions routing, InvestigationOptions investigation, ModelPricingOptions pricing, string version = DeploymentVersion)
    {
        ArgumentNullException.ThrowIfNull(routing);
        ArgumentNullException.ThrowIfNull(investigation);
        ArgumentNullException.ThrowIfNull(pricing);

        return new ModelConfigurationDocument
        {
            Version = version,
            DefaultProvider = routing.Provider,
            Routing = new Dictionary<string, string>(routing.Routing, StringComparer.Ordinal),
            PromptVersion = investigation.PromptVersion,
            MaxModelTurns = investigation.MaxModelTurns,
            MaxOutputTokens = investigation.MaxOutputTokens,
            ShadowBaseline = investigation.ShadowBaseline,
            InputPricePerMillionTokens = pricing.InputPerMillionTokens,
            OutputPricePerMillionTokens = pricing.OutputPerMillionTokens
        };
    }

    public static void Apply(ModelRoutingOptions options, ModelConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(document);

        options.Provider = document.DefaultProvider;
        options.Routing.Clear();
        foreach ((string role, string provider) in document.Routing)
        {
            options.Routing[role] = provider;
        }
    }

    public static void Apply(InvestigationOptions options, ModelConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(document);

        options.PromptVersion = document.PromptVersion;
        options.MaxModelTurns = document.MaxModelTurns;
        options.MaxOutputTokens = document.MaxOutputTokens;
        options.ShadowBaseline = document.ShadowBaseline;
    }

    public static void Apply(ModelPricingOptions options, ModelConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(document);

        options.InputPerMillionTokens = document.InputPricePerMillionTokens;
        options.OutputPerMillionTokens = document.OutputPricePerMillionTokens;
    }

    /// <summary>Why the document cannot run with the providers this deployment has configured; empty when it can.</summary>
    public static IReadOnlyList<string> Problems(ModelConfigurationDocument document, IReadOnlyCollection<string> availableProviders)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(availableProviders);

        var problems = new List<string>();
        foreach (string provider in document.Routing.Values.Prepend(document.DefaultProvider).Distinct(StringComparer.Ordinal))
        {
            if (!availableProviders.Contains(provider))
            {
                problems.Add($"Provider '{provider}' is not configured in this deployment; available: {string.Join(", ", availableProviders)}.");
            }
        }

        if (document.Routing.Keys.FirstOrDefault(role => role == InvestigationRoles.Verifier || !InvestigationRoles.IsKnown(role)) is { } role)
        {
            problems.Add($"'{role}' is not a model-backed investigator role.");
        }

        if (!InvestigatorPrompt.IsKnown(document.PromptVersion))
        {
            problems.Add($"Prompt version '{document.PromptVersion}' is not one of {string.Join(", ", InvestigatorPrompt.Versions)}.");
        }

        if (document.MaxModelTurns is < 1 or > 20 || document.MaxOutputTokens is < 256 or > 32_000)
        {
            problems.Add("An investigation allows 1 to 20 model turns and 256 to 32,000 output tokens per turn.");
        }

        if (document.InputPricePerMillionTokens < 0 || document.OutputPricePerMillionTokens < 0
            || !double.IsFinite(document.InputPricePerMillionTokens) || !double.IsFinite(document.OutputPricePerMillionTokens))
        {
            problems.Add("Token prices are finite and not negative.");
        }

        return problems;
    }
}

/// <summary>Reads and publishes AgentWorker's control-plane document; the deployment configuration seeds it.</summary>
public sealed class ModelConfigurationService(
    VersionedDocumentPublisher<ModelConfigurationDocument> publisher,
    Func<ModelConfigurationDocument> seed,
    IReadOnlyCollection<string> availableProviders)
{
    public IReadOnlyCollection<string> AvailableProviders => availableProviders;

    public Task<DocumentState<ModelConfigurationDocument>> CurrentAsync(CancellationToken cancellationToken) =>
        publisher.CurrentAsync(seed, cancellationToken);

    public Task<IReadOnlyList<DocumentVersion>> HistoryAsync(CancellationToken cancellationToken) =>
        publisher.HistoryAsync(seed, 100, cancellationToken);

    public Task<Result<PublishedDocument<ModelConfigurationDocument>>> PublishAsync(ModelConfigurationDocument document, string expectedVersion, string author, CancellationToken cancellationToken) =>
        publisher.PublishAsync(document, expectedVersion, author, seed, candidate => ModelConfigurationMapping.Problems(candidate, availableProviders), cancellationToken);
}
