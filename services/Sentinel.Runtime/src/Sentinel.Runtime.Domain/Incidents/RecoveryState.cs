namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>Where recovery verification stands: no window yet, a window being observed, or a verdict.</summary>
public enum RecoveryState
{
    NotScheduled,
    Scheduled,
    Evaluated
}
