namespace Sentinel.Runtime.Application.Options;

/// <summary>
/// The escalation investigator (Phase E05): once per cycle, an abstention for ambiguity on evidence at least this complete
/// asks for the one observation that would separate the two explanations, before Sentinel escalates to responders.
/// </summary>
public sealed class EscalationOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Below this evidence completeness an abstention is for missing data, which one more query does not fix.</summary>
    public double MinimumCompleteness { get; set; } = 0.8;
}
