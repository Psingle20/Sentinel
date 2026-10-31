using Amazon.ECS;
using Amazon.EventBridge;
using Amazon.SQS;
using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reference.Scenarios.Application.Abstractions;
using Reference.Scenarios.Infrastructure.Environments.Aws;
using Reference.Scenarios.Infrastructure.Environments.Local;

namespace Reference.Scenarios.Infrastructure;

/// <summary>
/// The scenario runner's infrastructure: the reference environment <c>Reference:Environment</c> names, <c>local</c> by
/// default or <c>aws</c>. The AWS clients take local AWS's endpoint and a synthetic key from configuration and never
/// fall back to AWS's own endpoints or the machine's credential chain.
/// </summary>
public sealed class InfrastructureDependencyModule : IDependencyModule
{
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddTransient<LocalReferenceEnvironment>();
        string environment = configuration["Reference:Environment"] ?? LocalReferenceEnvironment.EnvironmentName;
        switch (environment)
        {
            case LocalReferenceEnvironment.EnvironmentName:
                services.AddTransient<IReferenceEnvironment>(provider => provider.GetRequiredService<LocalReferenceEnvironment>());
                break;
            case AwsReferenceEnvironment.EnvironmentName:
                var options = AwsEnvironmentOptions.From(configuration);
                services.AddSingleton(options);
                services.AddSingleton<AwsProvisioning>();
                services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient(options.Credentials(), new AmazonSQSConfig { ServiceURL = options.ServiceUrl.ToString(), AuthenticationRegion = options.Region }));
                services.AddSingleton<IAmazonECS>(_ => new AmazonECSClient(options.Credentials(), new AmazonECSConfig { ServiceURL = options.ServiceUrl.ToString(), AuthenticationRegion = options.Region }));
                services.AddSingleton<IAmazonEventBridge>(_ => new AmazonEventBridgeClient(options.Credentials(), new AmazonEventBridgeConfig { ServiceURL = options.ServiceUrl.ToString(), AuthenticationRegion = options.Region }));
                services.AddTransient<IReferenceEnvironment, AwsReferenceEnvironment>();
                break;
            default:
                throw new InvalidOperationException($"Reference:Environment is '{environment}'; it must be 'local' or 'aws'.");
        }
    }
}
