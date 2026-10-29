using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Runtime.Application.Options;

namespace Sentinel.Runtime.Application.ControlPlane;

/// <summary>Runtime's options exactly as deployed, before any control-plane document is applied.</summary>
public sealed class RuntimeDeploymentOptions(Func<RuntimeOptions> create)
{
    /// <summary>The options name of the deployment configuration alone, unchanged by the control plane.</summary>
    public const string OptionsName = "deployment";

    public RuntimeOptions Value => create();

    /// <summary>A fresh copy to apply a candidate document to.</summary>
    public RuntimeOptions Create() => create();
}
