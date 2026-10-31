namespace Reference.QueueConsumer.Queue;

/// <summary>
/// Where the SQS orders queue lives, from <c>Reference:Queue:Sqs</c>: local AWS's endpoint, never AWS's own, the
/// region, and the 12-digit access key that selects the account. The secret is synthetic; local AWS does not check it.
/// </summary>
public sealed class SqsQueueOptions
{
    public const string Section = "Reference:Queue:Sqs";

    public Uri? ServiceUrl { get; set; }

    public string Region { get; set; } = "us-east-1";

    public string AccessKey { get; set; } = "000000000000";

    public string SecretKey { get; set; } = "test-not-a-secret";

    public string QueueName { get; set; } = IOrderQueue.Name;

    public string DeadLetterQueueName { get; set; } = $"{IOrderQueue.Name}-dlq";

    /// <summary>How long a received message stays invisible before SQS returns it, unless it is deleted first.</summary>
    public int VisibilityTimeoutSeconds { get; set; } = 30;
}
