using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Reference.Common.Redis;
using Reference.Common.Revisions;
using Reference.Common.Telemetry;

namespace Reference.Checkout.Orders;

public sealed class OrderServiceClient(HttpClient http)
{
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromMilliseconds(600);

    public static Uri ResolveBaseAddress(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string address = configuration["services:order-service:http:0"]
            ?? configuration["Reference:OrderService"]
            ?? throw new InvalidOperationException("The order-service address is not configured.");
        return new Uri(address);
    }

    public async Task<bool> PlaceOrderAsync(string cartId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AttemptTimeout);
        try
        {
            using HttpResponseMessage response = await http.PostAsJsonAsync("/api/orders", new OrderRequest(cartId), timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
