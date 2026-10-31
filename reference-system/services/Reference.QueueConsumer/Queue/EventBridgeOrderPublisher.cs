using System.Text.Json;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Amazon.Runtime;

namespace Reference.QueueConsumer.Queue;

/// <summary>
/// Publishes an order as an EventBridge event on the configured bus (Phase E01): a rule on the bus routes it to the orders
/// queue, where the consumer receives the event and unwraps the order. The bus accepts an event whether or not a rule
/// matches it, so a route that stops matching loses orders silently; only an entry EventBridge refuses is a failure here.
/// </summary>
public sealed class EventBridgeOrderPublisher(IAmazonEventBridge events, EventBridgeOptions options) : IOrderPublisher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Route => "eventbridge";

    public async Task PublishAsync(OrderMessage order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        PutEventsResponse response;
        try
        {
            response = await events.PutEventsAsync(
                new PutEventsRequest
                {
                    Entries = [new PutEventsRequestEntry { EventBusName = options.Bus, Source = options.Source, DetailType = options.DetailType, Detail = JsonSerializer.Serialize(order, Json) }]
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException or TimeoutException)
        {
            throw new QueueUnavailableException($"The event bus {options.Bus} is unavailable.", exception);
        }

        if (response.FailedEntryCount is > 0)
        {
            string reason = response.Entries?.FirstOrDefault()?.ErrorMessage ?? "no reason given";
            throw new QueueUnavailableException($"The event bus {options.Bus} refused order {order.Id}: {reason}");
        }
    }
}
