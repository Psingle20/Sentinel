using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Investigations;

/// <summary>The hypothesis key vocabulary investigators share, so specialists' explanations merge (Phase 04).</summary>
public static class HypothesisKeys
{
    public static string Change(ObservationId changeObservationId) => $"change:{changeObservationId}";

    public static string RedisSaturation(ServiceId? culprit = null) => culprit is { } service ? $"redis-saturation:{service}" : "redis-saturation";

    public static string DependencyRegression(ServiceId dependency) => $"dependency-regression:{dependency}";

    public static string LoadChange(ServiceId service) => $"load-change:{service}";

    public const string ExternalDependency = "external-dependency";

    /// <summary>The key this key refines, or null for a top-level key.</summary>
    public static string? Parent(string? key)
    {
        int separator = key?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        return separator > 0 && !key!.StartsWith("change:", StringComparison.Ordinal) ? key[..separator] : null;
    }
}
