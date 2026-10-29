using Sentinel.AgentWorker.Application.Options;

namespace Sentinel.AgentWorker.Application.ControlPlane;

/// <summary>AgentWorker's model selection exactly as deployed, before any control-plane document is applied.</summary>
public sealed class ModelDeploymentOptions(Func<ModelRoutingOptions> routing, Func<InvestigationOptions> investigation, Func<ModelPricingOptions> pricing)
{
    /// <summary>The options name of the deployment configuration alone, unchanged by the control plane.</summary>
    public const string OptionsName = "deployment";

    /// <summary>The document the deployment configuration amounts to: the seed of the control-plane document.</summary>
    public ModelConfigurationDocument Seed() => ModelConfigurationMapping.From(routing(), investigation(), pricing());
}
