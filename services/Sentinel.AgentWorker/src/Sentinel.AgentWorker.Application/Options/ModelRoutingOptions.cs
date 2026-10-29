using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.AgentWorker.Application.Investigations;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.Contracts.Investigations;

namespace Sentinel.AgentWorker.Application.Options;

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

    /// <summary>
    /// One provider for every role, ignoring <see cref="Provider"/> and <see cref="Routing"/>: deployment configuration
    /// only, never part of the control-plane document. The end-to-end suites set it to the test double so that a live
    /// model configured in user secrets is never called by a test run.
    /// </summary>
    public string? Override { get; set; }

    public string ProviderFor(string role) => Override ?? (Routing.TryGetValue(role, out string? routed) ? routed : Provider);
}
