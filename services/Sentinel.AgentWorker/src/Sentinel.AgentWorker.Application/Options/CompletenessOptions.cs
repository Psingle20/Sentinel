using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Application.Options;

/// <summary>Configured as <c>Investigation:Completeness</c>.</summary>
public sealed class CompletenessOptions
{
    public const string Section = "Investigation:Completeness";

    /// <summary>The longest one query waits for its sources to catch up with the end of its range.</summary>
    public TimeSpan MaxWait { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How far behind the end of the range an answer may be and still count as merely late. A source
    /// further behind is lagging, not late, and waiting for it would only add load.
    /// </summary>
    public TimeSpan MaxLag { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>The total waiting one investigation may spend across all its queries.</summary>
    public TimeSpan WaitBudget { get; set; } = TimeSpan.FromSeconds(45);
}
