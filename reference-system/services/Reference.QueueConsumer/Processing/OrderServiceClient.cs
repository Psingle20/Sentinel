using Reference.QueueConsumer.Queue;
using Reference.QueueConsumer.Telemetry;

namespace Reference.QueueConsumer.Processing;

/// <summary>
/// Confirms each booked order with order-service. An attempt that takes longer than 800 ms is abandoned and retried at
/// once, with neither backoff nor jitter, until one succeeds: when order-service slows, every worker retries in step and
/// adds calls to the ones it is already struggling with.
/// </summary>
public sealed class OrderServiceClient(IHttpClientFactory factory, QueueTelemetry telemetry, ILogger<OrderServiceClient> logger)
{
    public const string Name = "order-service";

    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromMilliseconds(800);

    public static Uri ResolveBaseAddress(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string address = configuration["services:order-service:http:0"]
            ?? configuration["Reference:OrderService"]
            ?? throw new InvalidOperationException("The order-service address is not configured.");
        return new Uri(address);
    }

    public async Task ConfirmAsync(OrderMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        HttpClient http = factory.CreateClient(Name);
        var path = new Uri($"/api/orders/{Uri.EscapeDataString(message.OrderId)}/confirmation", UriKind.Relative);
        int attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(AttemptTimeout);
            try
            {
                using HttpResponseMessage response = await http.PostAsync(path, content: null, timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                logger.LogWarning("Attempt {Attempt} to confirm order {OrderId} was refused by order-service with {StatusCode}; retrying at once", attempt, message.OrderId, (int)response.StatusCode);
            }
            catch (Exception exception) when (exception is HttpRequestException || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Attempt {Attempt} to confirm order {OrderId} gave up on order-service after {TimeoutMilliseconds} ms; retrying at once", attempt, message.OrderId, (int)AttemptTimeout.TotalMilliseconds);
            }

            telemetry.Retried();
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
