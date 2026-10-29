using Sentinel.Executor.Api;
using Sentinel.Executor.Infrastructure.Messaging;
using Sentinel.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Verification can take a minute or more; another consumer must not reclaim the request meanwhile.
builder.AddSentinelMessaging("executor", configureStreams: streams => streams.ClaimIdleTime = TimeSpan.FromMinutes(5))
    .RegisterExecutorMessages();
DependencyContainer.RegisterAll(builder.Services, builder.Configuration, builder.Environment);

await using WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.MapOperatorEndpoints();
app.MapControllers();

await app.RunAsync();

public partial class Program;
