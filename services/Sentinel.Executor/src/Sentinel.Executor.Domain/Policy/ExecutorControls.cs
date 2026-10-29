using System.Globalization;

namespace Sentinel.Executor.Domain.Policy;

/// <summary>Operator controls independent of every model and workflow path (ADR-006).</summary>
public sealed record ExecutorControls(bool EmergencyStop, IReadOnlySet<string> DisabledActions)
{
    public static ExecutorControls None { get; } = new(false, new HashSet<string>(StringComparer.Ordinal));
}
