using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Reference.DeployController.Api;
using Shouldly;

namespace Reference.Tests;

/// <summary>The deploy controller holds the reference system's deploy and read tokens; without either one it does not start.</summary>
public sealed class DeployControllerConfigurationTests
{
    [Theory]
    [InlineData(null, "read-test-not-a-token")]
    [InlineData("deploy-test-not-a-token", null)]
    [InlineData("deploy-test-not-a-token", "   ")]
    public void The_deploy_controller_does_not_start_without_both_tokens(string? deployToken, string? readToken)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DeployController:DeployToken"] = deployToken,
            ["DeployController:ReadToken"] = readToken
        }).Build();
        var module = new ApiDependencyModule(new HostingEnvironment { EnvironmentName = Environments.Development });

        Should.Throw<InvalidOperationException>(() => module.RegisterServices(new ServiceCollection(), configuration))
            .Message.ShouldBe("DeployController:DeployToken and DeployController:ReadToken must be configured.");
    }
}
