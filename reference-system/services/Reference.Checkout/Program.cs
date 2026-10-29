using Reference.Common;
using Reference.Common.Control;
using Reference.Checkout.Orders;
using Reference.Checkout.Processing;
using Reference.Checkout.Redis;
using Reference.Checkout.Revisions;
using Reference.Checkout.Telemetry;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddReferenceDefaults(CheckoutRevisions.ServiceName, CheckoutRevisions.Baseline, CheckoutRevisions.All, CheckoutTelemetry.SourceName);
builder.Services.AddSingleton<CheckoutRedis>();
builder.Services.AddSingleton<CheckoutTelemetry>();
builder.Services.AddHttpClient<OrderServiceClient>(client => client.BaseAddress = OrderServiceClient.ResolveBaseAddress(builder.Configuration));
builder.Services.AddTransient<CheckoutProcessor>();

await using WebApplication app = builder.Build();

app.MapPost("/api/checkout", async (CheckoutRequest request, CheckoutProcessor processor, CancellationToken cancellationToken) =>
{
    CheckoutResult result = await processor.ProcessAsync(request.CartId, cancellationToken);
    return result.Outcome is "success" or "slow" ? Results.Ok(result) : Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapReferenceControl();
app.MapReferenceHealth();

await app.RunAsync();

public partial class Program;
