using Sentinel.AgentWorker.Api;
using Sentinel.AgentWorker.Infrastructure.Messaging;
using Sentinel.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// An investigation may run for minutes; another consumer must not reclaim it while it does. An incident's specialists
// each wait on a model, so they run side by side rather than one after another.
int concurrency = builder.Configuration.GetValue("AgentWorker:InvestigationConcurrency", 6);
builder.AddSentinelMessaging("agentworker", configureStreams: streams =>
{
    streams.ClaimIdleTime = TimeSpan.FromMinutes(5);
    streams.MaxConcurrency = concurrency;
}).RegisterAgentWorkerMessages();
DependencyContainer.RegisterAll(builder.Services, builder.Configuration, builder.Environment);

await using WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.MapOperatorEndpoints();
app.MapControllers();

await app.RunAsync();

public partial class Program;
