using Sentinel.Ingestion.Api;
using Sentinel.Ingestion.Infrastructure.Messaging;
using Sentinel.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSentinelMessaging("ingestion", configureStreams: streams => streams.MaxStreamLength = 20_000).RegisterIngestionMessages();
DependencyContainer.RegisterAll(builder.Services, builder.Configuration, builder.Environment);

await using WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.MapOperatorEndpoints();
app.MapControllers();

await app.RunAsync();

public partial class Program;
