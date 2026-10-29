using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.Contracts.Investigations;
using Sentinel.AgentWorker.Application.Options;

namespace Sentinel.AgentWorker.Application.ControlPlane;

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
