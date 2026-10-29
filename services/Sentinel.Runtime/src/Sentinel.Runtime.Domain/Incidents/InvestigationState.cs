namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>Where the current cycle's investigations stand: none asked, some still owed a report, or every one reported or timed out.</summary>
public enum InvestigationState
{
    NotStarted,
    Pending,
    Settled
}
