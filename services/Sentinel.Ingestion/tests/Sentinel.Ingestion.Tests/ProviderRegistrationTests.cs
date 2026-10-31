using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using Sentinel.Ingestion.Infrastructure.DependencyInjection;
using Sentinel.Ingestion.Infrastructure.Providers;
using Shouldly;

namespace Sentinel.Ingestion.Tests;

/// <summary>
/// Ingestion holds the provider credentials (ADR-007); without the deploy controller's token it does not start. The
/// AWS environment adds its own deployment, topology and metrics providers beside the local ones (Phase E01).
/// </summary>
public sealed class ProviderRegistrationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ingestion_does_not_start_without_the_deploy_controllers_token(string? token)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ingestion:Providers:Prometheus:Endpoint"] = "http://prometheus.test",
            ["Ingestion:Providers:DeployController:Endpoint"] = "http://deploy-controller.test",
            ["Ingestion:Providers:DeployController:Token"] = token
        }).Build();

        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddIngestionProviders(configuration))
            .Message.ShouldContain("Ingestion:Providers:DeployController:Token is not configured");
    }

    [Fact]
    public void The_aws_environment_adds_its_deployment_topology_and_metrics_providers_beside_the_local_ones()
    {
        using ServiceProvider local = Providers(aws: false);
        using ServiceProvider aws = Providers(aws: true);

        local.GetServices<IDeploymentProvider>().Select(provider => provider.Name).ShouldBe([DeployControllerProvider.ProviderName]);
        local.GetServices<ITopologyProvider>().Select(provider => provider.Name).ShouldBe([JaegerProvider.ProviderName]);
        local.GetServices<IMetricsProvider>().Select(provider => provider.Name).ShouldBe([PrometheusMetricsProvider.ProviderName]);
        aws.GetServices<IDeploymentProvider>().Select(provider => provider.Name).ShouldBe([DeployControllerProvider.ProviderName, EcsDeploymentProvider.ProviderName, EventBridgeRouteProvider.ProviderName], ignoreOrder: true);
        aws.GetServices<ITopologyProvider>().Select(provider => provider.Name).ShouldBe([JaegerProvider.ProviderName, AwsTopologyProvider.ProviderName], ignoreOrder: true);
        aws.GetServices<IMetricsProvider>().Select(provider => provider.Name).ShouldBe([PrometheusMetricsProvider.ProviderName, SqsQueueMetricsProvider.ProviderName], ignoreOrder: true);
    }

    [Fact]
    public void The_aws_environment_is_not_read_without_its_credentials()
    {
        IConfiguration configuration = Configuration(aws: true, accessKey: "");

        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddIngestionProviders(configuration))
            .Message.ShouldContain("Ingestion:Providers:Aws:AccessKey is not configured");
    }

    private static ServiceProvider Providers(bool aws)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddIngestionProviders(Configuration(aws, accessKey: "000000000000"));
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(bool aws, string accessKey)
    {
        var values = new Dictionary<string, string?>
        {
            ["Ingestion:Providers:Prometheus:Endpoint"] = "http://prometheus.test",
            ["Ingestion:Providers:Jaeger:Endpoint"] = "http://jaeger.test",
            ["Ingestion:Providers:DeployController:Endpoint"] = "http://deploy-controller.test",
            ["Ingestion:Providers:DeployController:Token"] = "test-not-a-token"
        };
        if (aws)
        {
            values["Ingestion:Providers:Aws:ServiceUrl"] = "http://local-aws.test:4566";
            values["Ingestion:Providers:Aws:AccessKey"] = accessKey;
            values["Ingestion:Providers:Aws:SecretKey"] = "test-not-a-secret";
            values["Ingestion:Providers:Aws:Clusters:0"] = "reference";
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
