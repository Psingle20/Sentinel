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

    public string ProviderFor(string role) => Routing.TryGetValue(role, out string? routed) ? routed : Provider;
}
