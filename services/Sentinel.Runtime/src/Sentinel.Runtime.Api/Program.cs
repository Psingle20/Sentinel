using Sentinel.Runtime.Api;
using Sentinel.Runtime.Infrastructure;
using Sentinel.Runtime.Infrastructure.Messaging;
using Sentinel.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The silo starts before the message consumers, so the first message finds its grains ready.
builder.AddRuntimeActors();
builder.AddSentinelMessaging("runtime").RegisterRuntimeMessages();
DependencyContainer.RegisterAll(builder.Services, builder.Configuration, builder.Environment);

await using WebApplication app = builder.Build();

app.MapDefaultEndpoints();
app.MapOperatorEndpoints();
app.MapControllers();

await app.RunAsync();

public partial class Program;
