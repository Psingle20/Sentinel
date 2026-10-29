using System.Globalization;

namespace Sentinel.Executor.Domain.Policy;

/// <summary>The versioned remediation policy (policy as code). Its version is recorded with every evaluation.</summary>
public sealed record PolicyDocument
{
    public required string Version { get; init; }

    public AutonomyMode DefaultMode { get; init; } = AutonomyMode.ApprovalRequired;

    /// <summary>Mode per <c>tenant/environment</c> or per environment, overriding <see cref="DefaultMode"/>.</summary>
    public IReadOnlyDictionary<string, AutonomyMode> EnvironmentModes { get; init; } = new Dictionary<string, AutonomyMode>(StringComparer.Ordinal);

    public IReadOnlyList<PolicyRule> Rules { get; init; } = [];

    public IReadOnlyList<ChangeFreeze> Freezes { get; init; } = [];
}
