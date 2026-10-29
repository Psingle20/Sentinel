using Sentinel.AgentWorker.Api;
using Sentinel.AgentWorker.Infrastructure.Messaging;
using Sentinel.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// An investigation may run for minutes; another consumer must not reclaim it while it does.
builder.AddSentinelMessaging("agentworker", configureStreams: streams => streams.ClaimIdleTime = TimeSpan.FromMinutes(5)).RegisterAgentWorkerMessages();
DependencyContainer.RegisterAll(builder.Services, builder.Configuration, builder.Environment);

await using WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.MapOperatorEndpoints();
app.MapControllers();

await app.RunAsync();

public partial class Program;
