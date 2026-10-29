using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.Contracts.Investigations;
using Sentinel.AgentWorker.Application.Options;

namespace Sentinel.AgentWorker.Application.ControlPlane;

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
