using System.Reflection;
using NetArchTest.Rules;
using Sentinel.Runtime.Api.Controllers;
using Sentinel.Testing.Architecture;
using Shouldly;

namespace Sentinel.Runtime.Architecture.Tests;

public sealed class LayerDependencyTests
{
    private const string ApplicationNamespace = "Sentinel.Runtime.Application";
    private const string InfrastructureNamespace = "Sentinel.Runtime.Infrastructure";
    private const string HostNamespace = "Sentinel.Runtime.Api";
    private const string WorkflowNamespace = "Sentinel.Runtime.Infrastructure.Workflows";

    private static readonly Assembly DomainAssembly = Sentinel.Runtime.Domain.AssemblyReference.Assembly;
    private static readonly Assembly ApplicationAssembly = Sentinel.Runtime.Application.AssemblyReference.Assembly;
    private static readonly Assembly InfrastructureAssembly = Sentinel.Runtime.Infrastructure.AssemblyReference.Assembly;
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
        "Sentinel.Gateway",
        "Sentinel.Ingestion",
        "Sentinel.AgentWorker",
        "Sentinel.Executor"
    ];

    // Rules 8 and 9 (ADR-020): consumers and controllers Runtime's own refactoring step migrates.
    private static readonly string[] PendingConsumers =
    [
        "Sentinel.Runtime.Infrastructure.ObservationRecordedHandler",
        "Sentinel.Runtime.Infrastructure.InvestigationCompletedHandler",
        "Sentinel.Runtime.Infrastructure.RemediationExecutedHandler"
    ];

    private static readonly string[] PendingControllers =
    [
        "Sentinel.Runtime.Api.Controllers.IncidentsController",
        "Sentinel.Runtime.Api.Controllers.MemoryController",
        "Sentinel.Runtime.Api.Controllers.RemediationController",
        "Sentinel.Runtime.Api.Controllers.ConfigurationController",
        "Sentinel.Runtime.Api.Controllers.AdminController"
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
    public void Workflow_code_performs_no_io_and_reaches_the_incident_only_through_activities()
    {
        // ADR-005: workflow code schedules activities and timers; it never touches grains, storage,
        // the network, the file system or the application layer directly.
        TestResult result = Types.InAssembly(InfrastructureAssembly)
            .That()
            .ResideInNamespace(WorkflowNamespace)
            .And()
            .HaveNameEndingWith("Workflow")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Orleans",
                "Npgsql",
                "StackExchange.Redis",
                "System.Net",
                "System.IO",
                "Microsoft.Extensions.DependencyInjection",
                ApplicationNamespace,
                "Sentinel.Runtime.Domain",
                $"{InfrastructureNamespace}.Grains",
                $"{InfrastructureNamespace}.Storage")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue($"Workflow code depends on forbidden namespaces: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Workflow_code_never_reads_the_clock_sleeps_or_draws_randomness_directly()
    {
        // Nondeterministic calls break replay; Workflow.UtcNow, Workflow.DelayAsync and Workflow.Random replace them.
        string[] forbidden =
        [
            "System.DateTime::get_Now",
            "System.DateTime::get_UtcNow",
            "System.DateTimeOffset::get_Now",
            "System.DateTimeOffset::get_UtcNow",
            "System.Threading.Tasks.Task::Delay",
            "System.Threading.Thread::Sleep",
            "System.Guid::NewGuid",
            "System.Random::",
            "System.TimeProvider::"
        ];

        using var module = Mono.Cecil.ModuleDefinition.ReadModule(InfrastructureAssembly.Location);
        Mono.Cecil.TypeDefinition[] workflows = [.. module.Types.Where(type => type.Namespace == WorkflowNamespace && type.Name.EndsWith("Workflow", StringComparison.Ordinal))];
        workflows.ShouldNotBeEmpty();

        string[] calls = [.. workflows
            .SelectMany(AllNestedTypes)
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand)
            .OfType<Mono.Cecil.MethodReference>()
            .Select(called => $"{called.DeclaringType.FullName}::{called.Name}")
            .Where(called => forbidden.Any(prefix => called.StartsWith(prefix, StringComparison.Ordinal)))
            .Distinct()];

        calls.ShouldBeEmpty();
    }

    private static IEnumerable<Mono.Cecil.TypeDefinition> AllNestedTypes(Mono.Cecil.TypeDefinition type) =>
        [type, .. type.NestedTypes.SelectMany(AllNestedTypes)];

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
        ArchitectureRules.Controllers(HostAssembly, HostNamespace + ".Controllers", typeof(ApiController), [ApplicationNamespace, InfrastructureNamespace, "Sentinel.Runtime.Domain"], PendingControllers).ShouldBeEmpty();

    [Fact]
    public void Program_maps_no_endpoint_itself() =>
        ArchitectureRules.ProgramEndpoints(ArchitectureRules.SourceFile("services/Sentinel.Runtime/src/Sentinel.Runtime.Api/Program.cs")).ShouldBeEmpty();

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
