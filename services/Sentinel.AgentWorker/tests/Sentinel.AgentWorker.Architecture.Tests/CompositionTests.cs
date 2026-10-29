using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sentinel.AgentWorker.Api;
using Sentinel.AgentWorker.Application;
using Sentinel.AgentWorker.Application.Abstractions.Providers;
using Sentinel.AgentWorker.Infrastructure;
using Sentinel.Testing.Composition;
using Shouldly;

namespace Sentinel.AgentWorker.Architecture.Tests;

/// <summary>The AgentWorker host as Program composes it, checked against the composition rules (ADR-020).</summary>
public sealed class CompositionTests : IDisposable
{
    private static readonly Assembly ApplicationAssembly = Sentinel.AgentWorker.Application.AssemblyReference.Assembly;
    private static readonly Assembly InfrastructureAssembly = Sentinel.AgentWorker.Infrastructure.AssemblyReference.Assembly;
    private static readonly Assembly HostAssembly = typeof(ApiDependencyModule).Assembly;

    private readonly HostComposition<Program> _host = new(new Dictionary<string, string?>
    {
        ["ConnectionStrings:agentworker-db"] = "Host=localhost;Database=agentworker",
        ["ConnectionStrings:messaging"] = "localhost:6379"
    });

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task Every_command_query_and_request_has_exactly_one_handler_or_processor_that_resolves() =>
        (await CompositionAssertions.HandlerProblemsAsync(_host.Services, _host.Provider, ApplicationAssembly)).ShouldBeEmpty();

    [Fact]
    public void Every_port_has_exactly_one_implementation() =>
        CompositionAssertions.PortProblems(_host.Services, [typeof(IModelProvider)], ApplicationAssembly).ShouldBeEmpty();

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
