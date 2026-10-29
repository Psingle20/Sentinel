using Sentinel.Gateway.Api;
using Sentinel.Gateway.Infrastructure.Messaging;
using Sentinel.ServiceDefaults;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddSentinelMessaging("api").RegisterGatewayMessages();
DependencyContainer.RegisterAll(builder.Services, builder.Configuration, builder.Environment);

await using WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseIdempotencyKeys();

app.MapDefaultEndpoints();
app.MapOperatorEndpoints();
app.MapOpenApi();
app.MapControllers();

await app.RunAsync();

public partial class Program;