using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>Replay levels (Phase 07): which one ran decides what a result may claim.</summary>
public enum ReplayLevel
{
    /// <summary>Level 1: rebuild state and projections from the event stream, and re-run recorded decisions.</summary>
    Domain,

    /// <summary>Level 2: return captured tool and provider results to reproduce the reasoning inputs.</summary>
    Investigation,

    /// <summary>Level 3: rerun a selected model and prompt version against frozen inputs.</summary>
    ModelReevaluation,

    /// <summary>Level 4: rerun the failure against the reference environment (the scenario runner).</summary>
    SystemSimulation
}
