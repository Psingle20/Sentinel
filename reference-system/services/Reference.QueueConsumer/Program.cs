using Reference.Common;
using Reference.Common.Control;
using Reference.QueueConsumer.Faults;
using Reference.QueueConsumer.Processing;
using Reference.QueueConsumer.Queue;
using Reference.QueueConsumer.Revisions;
using Reference.QueueConsumer.Telemetry;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddReferenceDefaults(QueueRevisions.ServiceName, QueueRevisions.Baseline, QueueRevisions.All, QueueTelemetry.SourceName);
builder.Services.AddOrderQueue(builder.Configuration);
builder.Services.AddSingleton<QueueTelemetry>();
builder.Services.AddSingleton<DuplicatePublication>();
builder.Services.AddHttpClient(OrderServiceClient.Name, client => client.BaseAddress = OrderServiceClient.ResolveBaseAddress(builder.Configuration));
builder.Services.AddSingleton<OrderServiceClient>();
builder.Services.AddHostedService<OrderProducer>();
builder.Services.AddHostedService<QueueWorker>();
builder.Services.AddHostedService<QueueSampler>();

await using WebApplication app = builder.Build();

app.MapGet("/api/queue", async (IOrderQueue queue, CancellationToken cancellationToken) => await queue.StatusAsync(cancellationToken));
app.MapGet("/api/queue/messages/{id}", async (string id, IOrderQueue queue, CancellationToken cancellationToken) => await queue.MessageAsync(id, cancellationToken));

if (app.MapReferenceControl() is { } control)
{
    control.MapPost("/queue/poison", async (IOrderQueue queue, TimeProvider time, CancellationToken cancellationToken) =>
    {
        OrderMessage poison = PoisonOrder.Create(time.GetUtcNow());
        await queue.EnqueueAsync(poison, cancellationToken);
        return new QuarantineResult([poison.Id]);
    });
    control.MapPost("/queue/quarantine", async (QuarantineRequest? request, IOrderQueue queue, CancellationToken cancellationToken) =>
        await queue.QuarantineAsync(request ?? new QuarantineRequest(), cancellationToken));
    control.MapPost("/queue/release", async (IOrderQueue queue, CancellationToken cancellationToken) => await queue.ReleaseAsync(cancellationToken));
    control.MapPost("/queue/duplicate-publication", (DuplicatePublicationRequest? request, DuplicatePublication duplicates) =>
    {
        duplicates.Start(TimeSpan.FromSeconds((request ?? new DuplicatePublicationRequest()).Seconds));
        return Results.NoContent();
    });
    control.MapPost("/queue/reset", async (IOrderQueue queue, DuplicatePublication duplicates, CancellationToken cancellationToken) =>
    {
        duplicates.Stop();
        await queue.ResetAsync(cancellationToken);
        return Results.NoContent();
    });
}

app.MapReferenceHealth();

await app.RunAsync();

public partial class Program;
