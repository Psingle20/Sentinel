using Reference.Common.Redis;
using Reference.Common.Telemetry;
using Reference.RedisExporter.Sampling;
using Reference.RedisExporter.Telemetry;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddReferenceTelemetry("redis-exporter", RedisClientSampler.SourceName);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(_ => RedisEndpoint.Parse(builder.Configuration["Reference:Redis"]));
builder.Services.AddSingleton<RedisClientSampler>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<RedisClientSampler>());
builder.Services.AddSingleton<RedisExporterTelemetry>();
builder.Services.AddHealthChecks();

await using WebApplication app = builder.Build();

app.Services.GetRequiredService<RedisExporterTelemetry>();

app.MapGet("/api/clients", (RedisClientSampler sampler) => new
{
    sampler.ConnectedClients,
    sampler.MaxClients,
    sampler.ClientsByName
});
app.MapHealthChecks("/health");

await app.RunAsync();
