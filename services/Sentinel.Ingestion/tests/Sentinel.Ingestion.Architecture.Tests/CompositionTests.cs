using System.Reflection;
using Sentinel.Testing.Composition;
using Shouldly;

namespace Sentinel.Ingestion.Architecture.Tests;

/// <summary>
/// The Ingestion host as Program composes it, checked against the composition rules (ADR-020). Its layer
/// modules, and the check that each registers only its own layer's types, arrive in Ingestion's own step (5).
/// </summary>
public sealed class CompositionTests : IDisposable
{
    private static readonly Assembly ApplicationAssembly = Sentinel.Ingestion.Application.AssemblyReference.Assembly;

    private readonly HostComposition<Program> _host = new(new Dictionary<string, string?>
    {
        ["ConnectionStrings:ingestion-db"] = "Host=localhost;Database=ingestion",
        ["ConnectionStrings:messaging"] = "localhost:6379",
        ["Ingestion:Providers:DeployController:Token"] = "composition-test-not-a-token"
    });

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Every_command_query_and_request_has_exactly_one_handler_or_processor_that_resolves() =>
        CompositionAssertions.HandlerProblems(_host.Services, _host.Provider, ApplicationAssembly).ShouldBeEmpty();

    [Fact]
    public void Every_port_has_exactly_one_implementation() =>
        CompositionAssertions.PortProblems(_host.Services, [], ApplicationAssembly).ShouldBeEmpty();

    [Fact]
    public void No_singleton_captures_a_scoped_service() =>
        CompositionAssertions.LifetimeProblems(_host.Services).ShouldBeEmpty();

    [Fact]
    public void No_hosted_service_or_consumer_is_registered_twice() =>
        CompositionAssertions.DuplicateProblems(_host.Services).ShouldBeEmpty();
}
