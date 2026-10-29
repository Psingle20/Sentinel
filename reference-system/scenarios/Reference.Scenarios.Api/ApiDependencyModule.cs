using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reference.Common.Control;
using Reference.Scenarios.Api.Clients;
using Reference.Scenarios.Api.Control;
using Reference.Scenarios.Api.Manifests;
using Reference.Scenarios.Api.Traffic;
using Reference.Scenarios.Application.Abstractions;

namespace Reference.Scenarios.Api;

/// <summary>
/// The scenario runner's host: the manifest catalog read from disk, the reference system's client,
/// the traffic generator, and controllers whose answers keep their minimal-API shapes, with the
/// controls only where failure controls are enabled.
/// </summary>
public sealed class ApiDependencyModule(IHostEnvironment environment) : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        string manifestDirectory = configuration["Reference:ManifestDirectory"] ?? Path.Combine(AppContext.BaseDirectory, "manifests");
        services.AddSingleton(ScenarioCatalogLoader.Load(manifestDirectory));
        services.TryAddSingleton(TimeProvider.System);
        services.AddFailureControl(configuration, environment);
        services.AddHttpClient<IReferenceControl, ReferenceControlClient>(client => client.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient(nameof(TrafficGenerator), client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<TrafficGenerator>();
        services.AddSingleton<ITraffic>(provider => provider.GetRequiredService<TrafficGenerator>());
        services.AddHostedService(provider => provider.GetRequiredService<TrafficGenerator>());
        services.AddHealthChecks();

        bool controls = FailureControl.From(configuration, environment).Enabled;
        services.AddControllers(mvc => mvc.Conventions.Add(new ReferenceControlConvention(controls)))
            .ConfigureApiBehaviorOptions(behavior => behavior.SuppressMapClientErrors = true);
    }
}
