using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sentinel.Executor.Api;
using Sentinel.Executor.Application;
using Sentinel.Executor.Infrastructure;
using Sentinel.Testing.Composition;
using Shouldly;

namespace Sentinel.Executor.Architecture.Tests;

/// <summary>The Executor host as Program composes it, checked against the composition rules (ADR-020).</summary>
public sealed class CompositionTests : IDisposable
{
    private static readonly Assembly ApplicationAssembly = Sentinel.Executor.Application.AssemblyReference.Assembly;
    private static readonly Assembly InfrastructureAssembly = Sentinel.Executor.Infrastructure.AssemblyReference.Assembly;
    private static readonly Assembly HostAssembly = typeof(ApiDependencyModule).Assembly;

    // The deploy adapter refuses to register without a token; this one is never sent anywhere.
    private readonly HostComposition<Program> _host = new(new Dictionary<string, string?>
    {
        ["ConnectionStrings:executor-db"] = "Host=localhost;Database=executor",
        ["ConnectionStrings:messaging"] = "localhost:6379",
        ["Executor:Adapters:DeployController:Token"] = "composition-test-not-a-token"
    });

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Every_command_query_and_request_has_exactly_one_handler_or_processor_that_resolves() =>
        CompositionAssertions.HandlerProblems(_host.Services, _host.Provider, ApplicationAssembly).ShouldBeEmpty();

    [Fact]
    public void Every_port_has_exactly_one_implementation() =>
        CompositionAssertions.PortProblems(_host.Services, [typeof(IRemediationAdapter)], ApplicationAssembly).ShouldBeEmpty();

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
        CompositionAssertions.ModuleProblems(new InfrastructureDependencyModule(), configuration, [], ApplicationAssembly, HostAssembly).ShouldBeEmpty();
        CompositionAssertions.ModuleProblems(new ApiDependencyModule(environment), configuration, [], ApplicationAssembly, InfrastructureAssembly).ShouldBeEmpty();
    }
}
