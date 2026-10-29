using Sentinel.Ingestion.Application.Options;

namespace Sentinel.Ingestion.Application.ControlPlane;

/// <summary>Ingestion's options exactly as deployed, before any control-plane document is applied.</summary>
public sealed class IngestionDeploymentOptions(Func<IngestionOptions> create)
{
    /// <summary>The options name of the deployment configuration alone, unchanged by the control plane.</summary>
    public const string OptionsName = "deployment";

    public IngestionOptions Value => create();

    /// <summary>A fresh copy to apply a candidate document to.</summary>
    public IngestionOptions Create() => create();
}
