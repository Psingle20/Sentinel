using Reference.Common;
using Reference.Common.Control;
using Reference.Orders.Faults;
using Reference.Orders.Processing;
using Reference.Orders.Revisions;
using Reference.Orders.Storage;
using Reference.Orders.Telemetry;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddReferenceDefaults(OrderRevisions.ServiceName, OrderRevisions.Baseline, OrderRevisions.All, OrderTelemetry.SourceName);
builder.Services.AddSingleton<OrderStore>();
builder.Services.AddSingleton<OrderTelemetry>();
builder.Services.AddTransient<OrderProcessor>();
builder.Services.AddSingleton<SlowConfirmations>();
builder.Services.AddTransient<OrderConfirmer>();

await using WebApplication app = builder.Build();

app.MapPost("/api/orders", async (OrderRequest request, OrderProcessor processor, CancellationToken cancellationToken) =>
{
    OrderResult? result = await processor.PlaceAsync(request.CartId, cancellationToken);
    return result is null ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Created($"/api/orders/{result.OrderId}", result);
});

app.MapPost("/api/orders/{orderId}/confirmation", async (string orderId, OrderConfirmer confirmer) =>
{
    await confirmer.ConfirmAsync(orderId);
    return Results.NoContent();
});

if (app.MapReferenceControl() is { } control)
{
    control.MapPost("/orders/slow-confirmations", (SlowConfirmationsRequest? request, SlowConfirmations slow) =>
    {
        slow.Start(TimeSpan.FromSeconds((request ?? new SlowConfirmationsRequest()).Seconds));
        return Results.NoContent();
    });
    control.MapPost("/orders/reset", (SlowConfirmations slow) =>
    {
        slow.Stop();
        return Results.NoContent();
    });
}

app.MapReferenceHealth();

await app.RunAsync();

public partial class Program;
