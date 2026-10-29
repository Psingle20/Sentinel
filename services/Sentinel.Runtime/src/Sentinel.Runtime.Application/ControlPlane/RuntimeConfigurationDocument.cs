using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;

namespace Sentinel.Runtime.Application.ControlPlane;

/// <summary>
/// Runtime's control-plane document (Phase 08): the SLO detection rules, the recovery signals each
/// service must restore, the service catalog, maintenance suppressions, which investigators run,
/// the workflow's timers and the consensus thresholds. Published versions apply over the deployment
/// configuration; timers are captured when a workflow starts, so a change affects new cycles only.
/// </summary>
public sealed record RuntimeConfigurationDocument : IVersionedDocument
{
    public required string Version { get; init; }

    public IReadOnlyList<DetectionRuleSetting> Detection { get; init; } = [];

    public IReadOnlyDictionary<string, IReadOnlyList<RecoverySignalSetting>> Recovery { get; init; } = new Dictionary<string, IReadOnlyList<RecoverySignalSetting>>();

    public ServiceCatalogSetting Catalog { get; init; } = new();

    public IReadOnlyList<SuppressionSetting> Suppressions { get; init; } = [];

    public InvestigationSetting Investigation { get; init; } = new();

    public WorkflowSetting Workflow { get; init; } = new();

    public ConsensusSetting Consensus { get; init; } = new();
}
