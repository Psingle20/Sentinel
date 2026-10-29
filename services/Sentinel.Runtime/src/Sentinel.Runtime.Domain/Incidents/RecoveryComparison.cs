namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>How a recovery signal is compared with its threshold.</summary>
public enum RecoveryComparison
{
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual
}
