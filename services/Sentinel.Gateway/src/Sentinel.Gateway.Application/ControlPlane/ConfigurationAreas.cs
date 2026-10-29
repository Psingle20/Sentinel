using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.ControlPlane;

/// <summary>The control-plane documents (Phase 08), each owned and validated by one service.</summary>
public static class ConfigurationAreas
{
    /// <summary>Runtime: SLO detection rules, recovery signals, the service catalog, investigators, workflow timers and consensus.</summary>
    public const string Runtime = "runtime";

    /// <summary>AgentWorker: model selection, prompt version, model budget, shadow baseline and token prices.</summary>
    public const string Models = "models";

    /// <summary>Ingestion: the metric, log and trace sources and their pull timings.</summary>
    public const string Sources = "sources";

    /// <summary>Api: tenants and environments, notification channels and routes.</summary>
    public const string Api = "api";

    public static readonly IReadOnlyList<string> All = [Runtime, Models, Sources, Api];

    /// <summary>The answer for an area that is not one of <see cref="All"/>; it reaches no service.</summary>
    public static readonly ControlPlaneResponse Unknown = new(404, null, $"The configuration areas are {string.Join(", ", All)}.");
}
