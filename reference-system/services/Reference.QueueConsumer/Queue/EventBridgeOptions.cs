namespace Reference.QueueConsumer.Queue;

/// <summary>
/// Where the producer publishes orders in the AWS reference environment, from <c>Reference:Queue:EventBridge</c>: the
/// local AWS's endpoint, never AWS's own, the region, the 12-digit access key that selects the account, the event bus,
/// and the source and detail type an order event carries, which the bus's rule routes to the orders queue. The secret is
/// synthetic; local AWS does not check it.
/// </summary>
public sealed class EventBridgeOptions
{
    public const string Section = "Reference:Queue:EventBridge";

    public Uri? ServiceUrl { get; set; }

    public string Region { get; set; } = "us-east-1";

    public string AccessKey { get; set; } = "000000000000";

    public string SecretKey { get; set; } = "test-not-a-secret";

    public string Bus { get; set; } = "default";

    public string Source { get; set; } = "reference.checkout";

    public string DetailType { get; set; } = "OrderPlaced";
}
