using Sentinel.Testing.Architecture;
using Shouldly;

namespace Sentinel.Testing.Tests;

/// <summary>The project-reference rule catches each reference it is named for, over project files written for the test.</summary>
public sealed class ProjectReferenceRuleTests : IDisposable
{
    private const string Service = "Sentinel.Sample";

    private readonly string _source = Path.Combine(Path.GetTempPath(), $"sentinel-rule-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_source))
        {
            Directory.Delete(_source, recursive: true);
        }
    }

    [Fact]
    public void Inward_references_and_shared_packages_pass()
    {
        Write("Domain", "Sentinel.Contracts.Primitives");
        Write("Application", "Sentinel.Sample.Domain", "Sentinel.Contracts");
        Write("Infrastructure", "Sentinel.Sample.Application", "CleanArchitecture.BuildingBlocks.Infrastructure.Redis");
        Write("Api", "Sentinel.Sample.Infrastructure", "Sentinel.ServiceDefaults");

        ArchitectureRules.ProjectReferences(Service, _source).ShouldBeEmpty();
    }

    [Fact]
    public void An_outward_reference_another_service_and_the_reference_system_are_reported()
    {
        Write("Domain", "Sentinel.Sample.Application");
        Write("Application", "Sentinel.Sample.Domain", "Sentinel.Runtime.Domain");
        Write("Infrastructure", "Sentinel.Sample.Application", "Reference.Common");
        Write("Api", "Sentinel.Sample.Infrastructure");

        IReadOnlyList<string> violations = ArchitectureRules.ProjectReferences(Service, _source);

        violations.Count.ShouldBe(3);
        violations.ShouldContain(violation => violation.Contains("Sentinel.Sample.Domain references Sentinel.Sample.Application", StringComparison.Ordinal));
        violations.ShouldContain(violation => violation.Contains("Sentinel.Runtime.Domain", StringComparison.Ordinal));
        violations.ShouldContain(violation => violation.Contains("Reference.Common", StringComparison.Ordinal));
    }

    private void Write(string layer, params string[] references)
    {
        string project = $"{Service}.{layer}";
        string directory = Path.Combine(_source, project);
        Directory.CreateDirectory(directory);
        string items = string.Concat(references.Select(reference => $"""<ProjectReference Include="..\{reference}\{reference}.csproj" />"""));
        File.WriteAllText(Path.Combine(directory, $"{project}.csproj"), $"""<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>{items}</ItemGroup></Project>""");
    }
}
