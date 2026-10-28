using System.Reflection;
using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;
using Shouldly;

namespace Sentinel.Contracts.Primitives.Tests;

public sealed class PackageBoundaryTests
{
    private static readonly Assembly PrimitivesAssembly = typeof(IncidentId).Assembly;

    [Fact]
    public void Primitives_reference_only_the_base_class_library()
    {
        string[] referenced = PrimitivesAssembly.GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();

        referenced.ShouldAllBe(name => name == "System" || name.StartsWith("System.", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_identifier_declares_a_json_converter()
    {
        Type[] identifierTypes = PrimitivesAssembly.GetTypes()
            .Where(type => type.GetInterfaces().Any(IsStronglyTypedId))
            .ToArray();

        identifierTypes.ShouldNotBeEmpty();
        identifierTypes.ShouldAllBe(type => type.GetCustomAttributes(typeof(JsonConverterAttribute), false).Length == 1);
    }

    private static bool IsStronglyTypedId(Type type) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IStronglyTypedId<,>);
}
