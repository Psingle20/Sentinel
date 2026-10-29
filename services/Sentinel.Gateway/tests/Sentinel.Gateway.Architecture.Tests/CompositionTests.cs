using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sentinel.Gateway.Api;
using Sentinel.Gateway.Application;
using Sentinel.Gateway.Application.Live;
using Sentinel.Gateway.Infrastructure;
using Sentinel.Testing.Composition;
using Shouldly;

namespace Sentinel.Gateway.Architecture.Tests;

/// <summary>The Gateway host as Program composes it, checked against the composition rules (ADR-020).</summary>
public sealed class CompositionTests : IDisposable
{
    private static readonly Assembly ApplicationAssembly = Sentinel.Gateway.Application.AssemblyReference.Assembly;
    private static readonly Assembly InfrastructureAssembly = Sentinel.Gateway.Infrastructure.AssemblyReference.Assembly;
    private static readonly Assembly HostAssembly = typeof(ApiDependencyModule).Assembly;

    // Registered by the Infrastructure module until the Gateway's own step splits the control plane into processes, commands and queries.
    private static readonly string[] PendingInfrastructureRegistrations = ["Sentinel.Gateway.Application.ControlPlane.ApiConfigurationService"];

    private readonly HostComposition<Program> _host = new(new Dictionary<string, string?>
    {
        ["ConnectionStrings:api-db"] = "Host=localhost;Database=api",
        ["ConnectionStrings:messaging"] = "localhost:6379"
    });

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Every_command_query_and_request_has_exactly_one_handler_or_processor_that_resolves() =>
        (await CompositionAssertions.HandlerProblemsAsync(_host.Services, _host.Provider, ApplicationAssembly)).ShouldBeEmpty();

    [Fact]
    public void Every_port_has_exactly_one_implementation() =>
        CompositionAssertions.PortProblems(_host.Services, [typeof(INotificationProvider)], ApplicationAssembly).ShouldBeEmpty();

    [Fact]
    public void No_singleton_captures_a_scoped_service() =>
        CompositionAssertions.LifetimeProblems(_host.Services).ShouldBeEmpty();

    [Fact]
    public void No_hosted_service_or_consumer_is_registered_twice() =>
        CompositionAssertions.DuplicateProblems(_host.Services).ShouldBeEmpty();

    [Fact]
    public void Each_layer_module_registers_only_what_its_layer_owns()
    {
        IConfiguration configuration = _host.Provider.GetRequiredService<IConfiguration>();
        IHostEnvironment environment = _host.Provider.GetRequiredService<IHostEnvironment>();

        CompositionAssertions.ModuleProblems(new ApplicationDependencyModule(), configuration, [], InfrastructureAssembly, HostAssembly).ShouldBeEmpty();
        CompositionAssertions.ModuleProblems(new InfrastructureDependencyModule(), configuration, PendingInfrastructureRegistrations, ApplicationAssembly, HostAssembly).ShouldBeEmpty();
        CompositionAssertions.ModuleProblems(new ApiDependencyModule(environment), configuration, [], ApplicationAssembly, InfrastructureAssembly).ShouldBeEmpty();
    }
}
