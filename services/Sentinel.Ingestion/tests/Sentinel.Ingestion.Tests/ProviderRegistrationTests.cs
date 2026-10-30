using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Ingestion.Infrastructure.DependencyInjection;
using Shouldly;

namespace Sentinel.Ingestion.Tests;

/// <summary>Ingestion holds the provider credentials (ADR-007); without the deploy controller's token it does not start.</summary>
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
}
