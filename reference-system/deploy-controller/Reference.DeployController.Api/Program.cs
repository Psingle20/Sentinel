using Reference.Common.Telemetry;
using Reference.DeployController.Api;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddReferenceTelemetry("deploy-controller");
DependencyContainer.RegisterAll(builder.Services, builder.Configuration, builder.Environment);

await using WebApplication app = builder.Build();

app.MapControllers();
app.MapHealthChecks("/health");

await app.RunAsync();

public partial class Program;
