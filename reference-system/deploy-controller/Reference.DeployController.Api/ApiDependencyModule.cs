using System.Text.Json;
using System.Text.Json.Serialization;
using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reference.Common.Control;
using Reference.DeployController.Api.Clients;
using Reference.DeployController.Api.Control;
using Reference.DeployController.Api.Rollouts;
using Reference.DeployController.Application.Abstractions;
using Reference.DeployController.Application.Options;

namespace Reference.DeployController.Api;

/// <summary>
/// The deploy controller's host: its validated options, the revision endpoints of the reference
/// services, the rollout timer, and controllers whose answers keep their minimal-API shapes, with the
/// reference controls only where failure controls are enabled.
/// </summary>
public sealed class ApiDependencyModule(IHostEnvironment environment) : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        DeployControllerOptions options = configuration.GetSection(DeployControllerOptions.Section).Get<DeployControllerOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(options.DeployToken) || string.IsNullOrWhiteSpace(options.ReadToken))
        {
            throw new InvalidOperationException("DeployController:DeployToken and DeployController:ReadToken must be configured.");
        }

        // The controller deploys the services the AppHost gives it an address for; in the AWS environment the
        // queue-consumer is an ECS service and deploys through ECS instead (Phase E01).
        foreach (string service in options.Services.Keys.Where(service => configuration[$"services:{service}:http:0"] is null).ToList())
        {
            options.Services.Remove(service);
        }

        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddFailureControl(configuration, environment);
        services.AddHttpClient<IRevisionSwitch, RevisionClient>(client => client.Timeout = TimeSpan.FromSeconds(5));
        services.AddSingleton<IRolloutScheduler, RolloutScheduler>();
        services.AddHealthChecks();

        bool controls = FailureControl.From(configuration, environment).Enabled;
        services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
        services.AddControllers(mvc => mvc.Conventions.Add(new ReferenceControlConvention(controls)))
            .AddJsonOptions(json => json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)))
            .ConfigureApiBehaviorOptions(behavior => behavior.SuppressMapClientErrors = true);
    }
}
