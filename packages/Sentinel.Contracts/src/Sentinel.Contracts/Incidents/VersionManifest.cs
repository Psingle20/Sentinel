using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>
/// The versions a run was made with (Phase 07): enough to tell whether two runs are comparable and
/// to select the same model, prompt, tools, algorithm and policy again for a replay.
/// </summary>
public sealed record VersionManifest
{
    /// <summary>The incident-memory and run-record schema version.</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>The decision algorithm, for example <c>consensus-v1</c>.</summary>
    public string? DecisionAlgorithm { get; init; }

    /// <summary>Every model that reported, with its prompt version, in first-report order.</summary>
    public IReadOnlyList<ModelMetadata> Models { get; init; } = [];

    /// <summary>The investigation tool catalog the investigators used.</summary>
    public IReadOnlyList<string> ToolCatalogVersions { get; init; } = [];

    public string? PolicyVersion { get; init; }

    /// <summary>The redaction policy applied to the observations behind the evidence.</summary>
    public string? RedactionPolicyVersion { get; init; }

    /// <summary>The scenario package or dataset version, when the run was an evaluation run.</summary>
    public string? Dataset { get; init; }
}
