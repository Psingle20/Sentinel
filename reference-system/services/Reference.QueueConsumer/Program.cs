using Reference.Common;
using Reference.Common.Control;
using Reference.QueueConsumer.Faults;
using Reference.QueueConsumer.Processing;
using Reference.QueueConsumer.Queue;
using Reference.QueueConsumer.Revisions;
using Reference.QueueConsumer.Telemetry;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddReferenceDefaults(QueueRevisions.ServiceName, QueueRevisions.Baseline, QueueRevisions.All, QueueTelemetry.SourceName);
builder.Services.AddSingleton<OrderQueue>();
builder.Services.AddSingleton<QueueTelemetry>();
builder.Services.AddHostedService<OrderProducer>();
builder.Services.AddHostedService<QueueWorker>();
builder.Services.AddHostedService<QueueSampler>();

await using WebApplication app = builder.Build();

app.MapGet("/api/queue", async (OrderQueue queue, CancellationToken cancellationToken) => await queue.StatusAsync(cancellationToken));
app.MapGet("/api/queue/messages/{id}", async (string id, OrderQueue queue, CancellationToken cancellationToken) => await queue.MessageAsync(id, cancellationToken));

if (app.MapReferenceControl() is { } control)
{
    control.MapPost("/queue/poison", async (OrderQueue queue, TimeProvider time, CancellationToken cancellationToken) =>
    {
        OrderMessage poison = PoisonOrder.Create(time.GetUtcNow());
        await queue.EnqueueAsync(poison, cancellationToken);
        return new QuarantineResult([poison.Id]);
    });
    control.MapPost("/queue/quarantine", async (QuarantineRequest? request, OrderQueue queue, CancellationToken cancellationToken) =>
        await queue.QuarantineAsync(request ?? new QuarantineRequest(), cancellationToken));
    control.MapPost("/queue/release", async (OrderQueue queue, CancellationToken cancellationToken) => await queue.ReleaseAsync(cancellationToken));
    control.MapPost("/queue/reset", async (OrderQueue queue, CancellationToken cancellationToken) =>
    {
        await queue.ResetAsync(cancellationToken);
        return Results.NoContent();
    });
}

app.MapReferenceHealth();

await app.RunAsync();

public partial class Program;
