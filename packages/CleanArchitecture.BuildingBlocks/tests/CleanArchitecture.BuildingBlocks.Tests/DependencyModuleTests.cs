using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Tests;

public sealed class DependencyModuleTests
{
    private sealed class RecordingModule(string name, List<string> order) : IDependencyModule
    {
        public void RegisterServices(IServiceCollection services, IConfiguration configuration)
        {
            order.Add(name);
            services.AddSingleton(new Registered(name, configuration["Layer:Setting"]));
        }
    }

    private sealed record Registered(string Module, string? Setting);

    [Fact]
    public void Modules_register_in_the_order_the_container_gives_with_the_hosts_configuration()
    {
        List<string> order = [];
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Layer:Setting"] = "bound" }).Build();
        ServiceCollection services = new();

        services.AddModules(configuration, new RecordingModule("application", order), new RecordingModule("infrastructure", order), new RecordingModule("host", order));

        order.ShouldBe(["application", "infrastructure", "host"]);
        services.BuildServiceProvider().GetServices<Registered>().ShouldAllBe(registered => registered.Setting == "bound");
    }

    [Fact]
    public void A_missing_module_is_rejected()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        Should.Throw<ArgumentNullException>(() => new ServiceCollection().AddModules(configuration, (IDependencyModule)null!));
    }
}
