namespace Reference.QueueConsumer.Revisions;

/// <summary>The consumer's revisions. Both process orders the same way: v1.4.1 is the benign deployment of S6.</summary>
public static class QueueRevisions
{
    public const string ServiceName = "queue-consumer";
    public const string Baseline = "v1.4.0";

    public static readonly IReadOnlyCollection<string> All = [Baseline, "v1.4.1"];
}
