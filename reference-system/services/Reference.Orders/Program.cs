using Reference.Common;
using Reference.Common.Control;
using Reference.Orders;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddReferenceDefaults(OrderRevisions.ServiceName, OrderRevisions.Baseline, OrderRevisions.All, OrderTelemetry.SourceName);
builder.Services.AddSingleton<OrderStore>();
builder.Services.AddSingleton<OrderTelemetry>();
builder.Services.AddTransient<OrderProcessor>();

await using WebApplication app = builder.Build();

app.MapPost("/api/orders", async (OrderRequest request, OrderProcessor processor, CancellationToken cancellationToken) =>
{
    OrderResult? result = await processor.PlaceAsync(request.CartId, cancellationToken);
    return result is null ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.Created($"/api/orders/{result.OrderId}", result);
});

app.MapReferenceControl();
app.MapReferenceHealth();

await app.RunAsync();

public partial class Program;
