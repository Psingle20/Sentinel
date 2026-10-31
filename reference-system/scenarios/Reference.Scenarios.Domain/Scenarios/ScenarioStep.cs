using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reference.Scenarios.Domain.Scenarios;

public sealed record ScenarioStep
{
    public required int AtSeconds { get; init; }

    /// <summary><c>deploy</c>, <c>configChange</c>, <c>startBatch</c>, <c>stopBatch</c>, <c>poisonMessage</c>, <c>duplicatePublication</c>, <c>slowConfirmations</c>, <c>driftRoute</c> or <c>plantLog</c>.</summary>
    public required string Action { get; init; }

    public required string Service { get; init; }

    public string? Version { get; init; }

    public int? RolloutSeconds { get; init; }

    public int? TargetConnections { get; init; }

    public double? ConnectionsPerSecond { get; init; }

    public string? ConfigurationDiff { get; init; }

    /// <summary>How long a fault that lasts, such as a duplicate publication or slow confirmations, goes on.</summary>
    public int? DurationSeconds { get; init; }

    /// <summary>The line a <c>plantLog</c> step makes the service log, such as an instruction to AI agents.</summary>
    public string? Message { get; init; }

    /// <summary><c>cause</c>, <c>confounder</c> or <c>noise</c>.</summary>
    public required string Role { get; init; }
}
