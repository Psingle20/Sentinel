using System.Reflection;
using NetArchTest.Rules;
using Sentinel.Gateway.Api.Controllers;
using Sentinel.Testing.Architecture;
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

    // Rules 8 and 9 (ADR-020): consumers and controllers the Gateway's own refactoring step migrates.
    private static readonly string[] PendingConsumers =
    [
        "Sentinel.Gateway.Infrastructure.IncidentOpenedHandler",
        "Sentinel.Gateway.Infrastructure.IncidentStatusChangedHandler",
        "Sentinel.Gateway.Infrastructure.IncidentBlackboardUpdatedHandler",
        "Sentinel.Gateway.Infrastructure.DecisionRecordedHandler",
        "Sentinel.Gateway.Infrastructure.RemediationPlanProposedHandler",
        "Sentinel.Gateway.Infrastructure.RecoveryEvaluatedHandler"
    ];

    private static readonly string[] PendingControllers =
    [
        "Sentinel.Gateway.Api.Controllers.IncidentsController",
        "Sentinel.Gateway.Api.Controllers.PostmortemsController",
        "Sentinel.Gateway.Api.Controllers.AgentExecutionsController",
        "Sentinel.Gateway.Api.Controllers.LiveController",
        "Sentinel.Gateway.Api.Controllers.AnalyticsController",
        "Sentinel.Gateway.Api.Controllers.AdminController"
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

    [Fact]
    public void Every_command_query_and_request_has_one_handler_or_processor_in_its_own_namespace() =>
        ArchitectureRules.HandlerPlacement(ApplicationAssembly).ShouldBeEmpty();

    [Fact]
    public void Processors_depend_only_on_a_validator_and_a_dispatcher() =>
        ArchitectureRules.ProcessorDependencies(ApplicationAssembly).ShouldBeEmpty();

    [Fact]
    public void Consumers_live_in_messaging_consumers_and_send_requests() =>
        ArchitectureRules.Consumers(InfrastructureAssembly, InfrastructureNamespace + ".Messaging.Consumers", PendingConsumers).ShouldBeEmpty();

    [Fact]
    public void Controllers_derive_from_the_api_controller_and_send_requests() =>
        ArchitectureRules.Controllers(HostAssembly, HostNamespace + ".Controllers", typeof(ApiController), [ApplicationNamespace, InfrastructureNamespace, "Sentinel.Gateway.Domain"], PendingControllers).ShouldBeEmpty();

    [Fact]
    public void Program_maps_no_endpoint_itself() =>
        ArchitectureRules.ProgramEndpoints(ArchitectureRules.SourceFile("services/Sentinel.Gateway/src/Sentinel.Gateway.Api/Program.cs")).ShouldBeEmpty();

    [Fact]
    public void Only_the_application_module_registers_the_building_blocks() =>
        ArchitectureRules.BuildingBlocksRegistration(ServiceAssemblies, []).ShouldBeEmpty();

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
