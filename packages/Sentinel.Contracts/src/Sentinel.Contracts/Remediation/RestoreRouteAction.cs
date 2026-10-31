namespace Sentinel.Contracts.Remediation;

/// <summary>
/// Restore an event route to its approved definition: the rule <see cref="Rule"/> on <see cref="EventBus"/> gets back the
/// pattern Executor holds as approved for it, so the events it stopped matching reach the target service's queue again
/// (Phase E01). The action names the rule, never a pattern, which only Executor holds. There is no automatic
/// compensation: drifting the route again is a human decision.
/// </summary>
public sealed record RestoreRouteAction : RemediationAction
{
    public required string Rule { get; init; }

    public required string EventBus { get; init; }

    /// <summary>Why the route is restored, for the person who approves it.</summary>
    public required string Reason { get; init; }
}
