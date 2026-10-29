using Reference.Common.Telemetry;
using Reference.Scenarios.Api;
using Reference.Scenarios.Api.Traffic;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddReferenceTelemetry("scenario-runner", TrafficGenerator.SourceName);
DependencyContainer.RegisterAll(builder.Services, builder.Configuration, builder.Environment);

await using WebApplication app = builder.Build();

app.MapControllers();
app.MapHealthChecks("/health");

await app.RunAsync();

public partial class Program;
