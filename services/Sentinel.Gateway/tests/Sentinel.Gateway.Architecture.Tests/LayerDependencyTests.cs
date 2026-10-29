using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace Sentinel.Gateway.Architecture.Tests;

public sealed class LayerDependencyTests
{
    private const string ApplicationNamespace = "Sentinel.Gateway.Application";
    private const string InfrastructureNamespace = "Sentinel.Gateway.Infrastructure";
    private const string HostNamespace = "Sentinel.Gateway.Api";

    private static readonly Assembly DomainAssembly = Sentinel.Gateway.Domain.AssemblyReference.Assembly;
    private static readonly Assembly ApplicationAssembly = Sentinel.Gateway.Application.AssemblyReference.Assembly;
    private static readonly Assembly InfrastructureAssembly = Sentinel.Gateway.Infrastructure.AssemblyReference.Assembly;
    private static readonly Assembly HostAssembly = Assembly.Load(HostNamespace);

    private static readonly Assembly[] ServiceAssemblies =
        [DomainAssembly, ApplicationAssembly, InfrastructureAssembly, HostAssembly];

    private static readonly string[] AllowedDomainReferences =
    [
        "CleanArchitecture.BuildingBlocks.Core",
        "Sentinel.Contracts.Primitives"
    ];

    private static readonly string[] TechnologyNamespaces =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "StackExchange.Redis",
        "Orleans",
        "Temporalio"
    ];

    private static readonly string[] OtherServiceNamespaces =
    [
        "Sentinel.Ingestion",
        "Sentinel.Runtime",
        "Sentinel.AgentWorker",
        "Sentinel.Executor"
    ];

    [Fact]
    public void Domain_references_only_core_building_blocks_and_contract_primitives()
    {
        string[] referenced = DomainAssembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        referenced.ShouldAllBe(name => IsBaseClassLibrary(name) || AllowedDomainReferences.Contains(name));
    }

    [Fact]
    public void Domain_does_not_depend_on_outer_layers()
    {
        AssertNoDependency(DomainAssembly, ApplicationNamespace, InfrastructureNamespace, HostNamespace);
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_the_host_or_technology_sdks()
    {
        AssertNoDependency(ApplicationAssembly, [InfrastructureNamespace, HostNamespace, .. TechnologyNamespaces]);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_host()
    {
        AssertNoDependency(InfrastructureAssembly, HostNamespace);
    }

    [Fact]
    public void No_layer_depends_on_another_service()
    {
        foreach (Assembly assembly in ServiceAssemblies)
        {
            AssertNoDependency(assembly, OtherServiceNamespaces);
        }
    }

    [Fact]
    public void No_layer_depends_on_the_reference_system()
    {
        // Sentinel reaches the reference system only through telemetry and the deploy controller's API (ADR-011).
        foreach (Assembly assembly in ServiceAssemblies)
        {
            AssertNoDependency(assembly, "Reference.");
            assembly.GetReferencedAssemblies().ShouldNotContain(name => (name.Name ?? string.Empty).StartsWith("Reference.", StringComparison.Ordinal));
        }
    }

    private static void AssertNoDependency(Assembly assembly, params string[] forbiddenNamespaces)
    {
        TestResult result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenNamespaces)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"{assembly.GetName().Name} depends on forbidden namespaces: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    private static bool IsBaseClassLibrary(string name) =>
        name == "System" || name.StartsWith("System.", StringComparison.Ordinal);
}
