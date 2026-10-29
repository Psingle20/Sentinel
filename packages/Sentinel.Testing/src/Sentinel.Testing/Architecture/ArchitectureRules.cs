using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using FluentValidation;
using NetArchTest.Rules;

// The rules read private fields and attributes on purpose: a transition table or a framework attribute is
// what they look for, wherever it is declared.
#pragma warning disable S3011

namespace Sentinel.Testing.Architecture;

/// <summary>
/// Rules 6 to 13 of the application composition guide's enforceable rules (ADR-020), shared by every
/// service's architecture tests. Each returns its violations. <c>pending</c> names types, by full
/// name, that a later refactoring step migrates; they are not reported, and a pending entry that no
/// longer exists or already complies is reported instead, so the list can only shrink.
/// </summary>
public static partial class ArchitectureRules
{
    private static readonly (Type Message, Type Handler)[] HandlerShapes =
    [
        (typeof(ICommand), typeof(ICommandHandler<>)),
        (typeof(ICommand<>), typeof(ICommandHandler<,>)),
        (typeof(IQuery<>), typeof(IQueryHandler<,>)),
        (typeof(IRequest), typeof(IProcessor<>)),
        (typeof(IRequest<>), typeof(IProcessor<,>))
    ];

    private static readonly Type[] Dispatchers = [typeof(ICommandDispatcher), typeof(IQueryDispatcher)];

    private static readonly string[] FrameworkAttributeNamespaces =
    [
        "System.Text.Json.Serialization",
        "System.ComponentModel.DataAnnotations",
        "System.Runtime.Serialization",
        "Microsoft.EntityFrameworkCore",
        "Newtonsoft.Json",
        "Orleans"
    ];

    private static readonly Type[] AllowedDomainAttributes = [typeof(JsonDerivedTypeAttribute), typeof(JsonPolymorphicAttribute)];

    // The Sentinel packages a service may reference (ADR-007); everything else named Sentinel.* belongs to a service.
    private static readonly string[] SharedPackages = ["Sentinel.Contracts", "Sentinel.Contracts.Primitives", "Sentinel.ServiceDefaults"];

    /// <summary>Rule 6: every command, query and request has exactly one handler or processor, in its own namespace.</summary>
    public static IReadOnlyList<string> HandlerPlacement(Assembly application)
    {
        ArgumentNullException.ThrowIfNull(application);

        Type[] types = Concrete(application);
        var problems = new List<string>();
        foreach (Type message in types)
        {
            foreach (Type handler in HandlersOf(message))
            {
                Type[] implementations = [.. types.Where(handler.IsAssignableFrom)];
                if (implementations.Length != 1)
                {
                    problems.Add($"{message.FullName} has {implementations.Length} implementations of {handler.Name}; exactly one is required.");
                }
                else if (implementations[0].Namespace != message.Namespace)
                {
                    problems.Add($"{implementations[0].FullName} handles {message.FullName} from another namespace; they belong in one feature folder.");
                }
            }
        }

        return problems;
    }

    /// <summary>Rule 7: a processor depends only on a validator and a dispatcher.</summary>
    public static IReadOnlyList<string> ProcessorDependencies(Assembly application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return [.. Concrete(application)
            .Where(type => type.GetInterfaces().Any(IsProcessor))
            .SelectMany(processor => Parameters(processor)
                .Where(parameter => !Dispatchers.Contains(parameter) && !IsValidator(parameter))
                .Select(parameter => $"Processor {processor.FullName} depends on {parameter.Name}; a processor depends only on a validator and a dispatcher."))];
    }

    /// <summary>Rule 8: message consumers live in <c>Infrastructure.Messaging.Consumers</c> and depend only on <see cref="IRequestDispatcher"/>.</summary>
    public static IReadOnlyList<string> Consumers(Assembly infrastructure, string consumersNamespace, IReadOnlyCollection<string> pending)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        ArgumentNullException.ThrowIfNull(pending);

        Type[] consumers = [.. Concrete(infrastructure).Where(type => type.GetInterfaces().Any(IsConsumer))];
        return Judge(consumers.Select(consumer => (consumer, (IReadOnlyList<string>)ConsumerProblems(consumer, consumersNamespace))), pending);
    }

    /// <summary>
    /// Rule 9: controllers derive from the service's <c>ApiController</c>, reach the application only
    /// through <see cref="IRequestDispatcher"/>, and depend on nothing from <paramref name="forbiddenNamespaces"/>.
    /// A controller with no dependencies, one that only answers from the caller's own claims, passes.
    /// </summary>
    public static IReadOnlyList<string> Controllers(Assembly api, string controllersNamespace, Type apiController, IReadOnlyCollection<string> forbiddenNamespaces, IReadOnlyCollection<string> pending)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(apiController);
        ArgumentNullException.ThrowIfNull(forbiddenNamespaces);

        Type[] controllers = [.. Concrete(api).Where(type => type.Namespace == controllersNamespace && type != apiController)];
        return Judge(controllers.Select(controller => (controller, (IReadOnlyList<string>)ControllerProblems(controller, apiController, forbiddenNamespaces))), pending);
    }

    /// <summary>
    /// Rule 9, second half: the host's <c>Program.cs</c> is composition and middleware, with no inline
    /// endpoint. A host whose endpoints a later step moves is <paramref name="pending"/>: it is
    /// reported only once it maps none inline, so the flag is removed.
    /// </summary>
    public static IReadOnlyList<string> ProgramEndpoints(string programSource, bool pending = false)
    {
        ArgumentNullException.ThrowIfNull(programSource);

        string file = Path.GetFileName(programSource);
        Match[] inline = [.. InlineEndpoint().Matches(File.ReadAllText(programSource)).Cast<Match>()];
        if (pending)
        {
            return inline.Length == 0 ? [$"{file} maps no endpoint inline any more; it is no longer pending."] : [];
        }

        return [.. inline.Select(match => $"{file} maps an endpoint inline ({match.Value.Trim()}); it belongs on a controller.")];
    }

    /// <summary>Rule 10: only <c>ApplicationDependencyModule</c> calls <c>AddBuildingBlocks</c>.</summary>
    public static IReadOnlyList<string> BuildingBlocksRegistration(IEnumerable<Assembly> assemblies, IReadOnlyCollection<string> pending)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(pending);

        Type[] registering = [.. assemblies.SelectMany(assembly => Types.InAssembly(assembly)
            .That()
            .HaveDependencyOn(typeof(BuildingBlocksServiceCollectionExtensions).FullName)
            .GetTypes())];
        return Judge(
            registering.Select(type => (type, (IReadOnlyList<string>)(type.Name == "ApplicationDependencyModule" ? [] : [$"{type.FullName} calls AddBuildingBlocks; only ApplicationDependencyModule may."]))),
            pending);
    }

    /// <summary>
    /// Rule 11: domain classes are sealed or abstract and expose no public setter (an init accessor
    /// sets a value at construction and is allowed), and every <c>&lt;Name&gt;State</c> enum has a
    /// transition table in its own namespace: a static field mapping each state to the states it may
    /// move to.
    /// </summary>
    public static IReadOnlyList<string> DomainModel(Assembly domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        Type[] types = [.. domain.GetTypes().Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), false))];
        var problems = new List<string>();
        foreach (Type type in types.Where(type => type.IsClass))
        {
            if (!type.IsSealed && !type.IsAbstract)
            {
                problems.Add($"{type.FullName} is neither sealed nor abstract.");
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (property.SetMethod is { IsPublic: true } setter && !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
                {
                    problems.Add($"{type.FullName}.{property.Name} has a public setter.");
                }
            }
        }

        foreach (Type state in types.Where(type => type.IsEnum && type.Name.EndsWith("State", StringComparison.Ordinal)))
        {
            bool tabled = types
                .Where(type => type.Namespace == state.Namespace)
                .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Any(field => IsTransitionTable(field.FieldType, state));
            if (!tabled)
            {
                problems.Add($"{state.FullName} has no transition table in {state.Namespace}.");
            }
        }

        return problems;
    }

    /// <summary>
    /// Rule 12: an error whose code names an aggregate (<c>&lt;Aggregate&gt;.…</c>) is declared in that
    /// aggregate's <c>&lt;Aggregate&gt;Errors</c> catalogue in Domain. It checks every error Application
    /// declares as a static field or property; an aggregate is a name with a catalogue in Domain.
    /// </summary>
    public static IReadOnlyList<string> ErrorCatalogues(Assembly domain, Assembly application)
    {
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(application);

        HashSet<string> aggregates = [.. domain.GetTypes()
            .Where(type => type is { IsAbstract: true, IsSealed: true } && type.Name.EndsWith("Errors", StringComparison.Ordinal))
            .Select(type => type.Name[..^"Errors".Length])];
        var problems = new List<string>();
        foreach (Type type in application.GetTypes().Where(type => !type.ContainsGenericParameters))
        {
            const BindingFlags Declared = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            IEnumerable<(string Name, Error? Error)> errors =
            [
                .. type.GetFields(Declared).Where(field => typeof(Error).IsAssignableFrom(field.FieldType)).Select(field => (field.Name, field.GetValue(null) as Error)),
                .. type.GetProperties(Declared).Where(property => typeof(Error).IsAssignableFrom(property.PropertyType) && property.GetIndexParameters().Length == 0).Select(property => (property.Name, property.GetValue(null) as Error))
            ];
            foreach ((string name, Error? error) in errors)
            {
                if (error is not null && aggregates.Contains(error.Code.Split('.')[0]))
                {
                    problems.Add($"{type.FullName}.{name} declares {error.Code}; it belongs in Domain's {error.Code.Split('.')[0]}Errors.");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// Rule 13: domain types carry no persistence, serialization or framework attributes, other than the
    /// polymorphism attributes on event-stream and aggregate-command base types.
    /// </summary>
    public static IReadOnlyList<string> DomainAttributes(Assembly domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var problems = new List<string>();
        foreach (Type type in domain.GetTypes().Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), false)))
        {
            IEnumerable<MemberInfo> members = [type, .. type.GetProperties(All), .. type.GetFields(All), .. type.GetMethods(All)];
            foreach (MemberInfo member in members)
            {
                foreach (CustomAttributeData attribute in member.GetCustomAttributesData())
                {
                    string ns = attribute.AttributeType.Namespace ?? string.Empty;
                    if (FrameworkAttributeNamespaces.Any(framework => ns == framework || ns.StartsWith(framework + ".", StringComparison.Ordinal))
                        && !AllowedDomainAttributes.Contains(attribute.AttributeType))
                    {
                        problems.Add($"{type.FullName}{(member == type ? string.Empty : "." + member.Name)} carries {attribute.AttributeType.Name}.");
                    }
                }
            }
        }

        return problems;
    }

    /// <summary>The source file of a project, found by walking up from the test's output directory.</summary>
    /// <summary>
    /// Project references, which the type rules above cannot see until a type uses them (review finding F-016): inside a
    /// service they point inward, Api to Infrastructure to Application to Domain, and no project references another
    /// service or the reference system. Shared packages are allowed here; the assembly rules govern which.
    /// </summary>
    public static IReadOnlyList<string> ProjectReferences(string service) =>
        ProjectReferences(service, Path.GetDirectoryName(Path.GetDirectoryName(SourceFile($"services/{service}/src/{service}.Domain/{service}.Domain.csproj")))!);

    /// <summary><see cref="ProjectReferences(string)"/> over the service's projects under <paramref name="source"/>.</summary>
    public static IReadOnlyList<string> ProjectReferences(string service, string source)
    {
        string[] layers = ["Domain", "Application", "Infrastructure", "Api"];
        var violations = new List<string>();
        for (int layer = 0; layer < layers.Length; layer++)
        {
            string project = $"{service}.{layers[layer]}";
            string file = Path.Combine(source, project, $"{project}.csproj");
            foreach (string referenced in System.Xml.Linq.XDocument.Load(file).Descendants("ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension((string?)reference.Attribute("Include") ?? string.Empty)))
            {
                if (referenced.StartsWith($"{service}.", StringComparison.Ordinal))
                {
                    int target = Array.IndexOf(layers, referenced[(service.Length + 1)..]);
                    if (target < 0 || target >= layer)
                    {
                        violations.Add($"{project} references {referenced}; references point inward only.");
                    }
                }
                else if (referenced.StartsWith("Reference.", StringComparison.Ordinal)
                    || referenced.StartsWith("Sentinel.", StringComparison.Ordinal) && !SharedPackages.Contains(referenced))
                {
                    violations.Add($"{project} references {referenced}, which belongs to another service or the reference system.");
                }
            }
        }

        return violations;
    }

    public static string SourceFile(string relativePath)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"{relativePath} was not found above {AppContext.BaseDirectory}.");
    }

    private static List<string> Judge(IEnumerable<(Type Type, IReadOnlyList<string> Problems)> results, IReadOnlyCollection<string> pending)
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach ((Type type, IReadOnlyList<string> found) in results)
        {
            string name = type.FullName ?? type.Name;
            seen.Add(name);
            if (pending.Contains(name))
            {
                if (found.Count == 0)
                {
                    problems.Add($"{name} now complies; remove it from the pending list.");
                }

                continue;
            }

            problems.AddRange(found);
        }

        problems.AddRange(pending.Where(name => !seen.Contains(name)).Select(name => $"Pending entry {name} no longer exists; remove it from the pending list."));
        return problems;
    }

    private static List<string> ConsumerProblems(Type consumer, string consumersNamespace)
    {
        var problems = new List<string>();
        if (consumer.Namespace != consumersNamespace)
        {
            problems.Add($"Consumer {consumer.FullName} belongs in {consumersNamespace}.");
        }

        Type[] parameters = Parameters(consumer);
        if (parameters.Length != 1 || parameters[0] != typeof(IRequestDispatcher))
        {
            problems.Add($"Consumer {consumer.FullName} depends on {string.Join(", ", parameters.Select(parameter => parameter.Name))}; a consumer depends only on IRequestDispatcher.");
        }

        return problems;
    }

    private static List<string> ControllerProblems(Type controller, Type apiController, IReadOnlyCollection<string> forbiddenNamespaces)
    {
        var problems = new List<string>();
        if (!controller.IsSubclassOf(apiController))
        {
            problems.Add($"Controller {controller.FullName} does not derive from {apiController.Name}.");
        }

        Type[] parameters = Parameters(controller);
        if (parameters.Length > 0 && !parameters.Contains(typeof(IRequestDispatcher)))
        {
            problems.Add($"Controller {controller.FullName} does not send requests through IRequestDispatcher.");
        }

        foreach (Type parameter in parameters.Where(parameter => parameter != typeof(IRequestDispatcher)))
        {
            if (forbiddenNamespaces.Any(forbidden => parameter.Namespace?.StartsWith(forbidden, StringComparison.Ordinal) == true))
            {
                problems.Add($"Controller {controller.FullName} depends on {parameter.FullName}; controllers reach the application only through requests.");
            }
        }

        return problems;
    }

    // A map from a state to the states it may move to: a dictionary keyed by the state whose values are its states.
    private static bool IsTransitionTable(Type fieldType, Type state) =>
        fieldType.IsGenericType
        && fieldType.GetGenericArguments() is [Type key, Type value]
        && key == state
        && (value == state.MakeArrayType() || value.IsGenericType && value.GetGenericArguments() is [Type element] && element == state);

    private static Type[] Parameters(Type type) =>
        [.. type.GetConstructors().SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType).Distinct()];

    private static Type[] Concrete(Assembly assembly) =>
        [.. assembly.GetTypes().Where(type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false } && !type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false))];

    private static bool IsProcessor(Type type) =>
        type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(IProcessor<>) || type.GetGenericTypeDefinition() == typeof(IProcessor<,>));

    private static bool IsConsumer(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IIntegrationMessageHandler<>);

    private static bool IsValidator(Type type) =>
        type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(IValidator<>) || type.GetGenericTypeDefinition() == typeof(IEnumerable<>) && IsValidator(type.GetGenericArguments()[0]));

    private static IEnumerable<Type> HandlersOf(Type message)
    {
        foreach (Type implemented in message.GetInterfaces())
        {
            foreach ((Type shape, Type handler) in HandlerShapes)
            {
                if (implemented == shape)
                {
                    yield return handler.MakeGenericType(message);
                }
                else if (implemented.IsGenericType && implemented.GetGenericTypeDefinition() == shape)
                {
                    yield return handler.MakeGenericType(message, implemented.GetGenericArguments()[0]);
                }
            }
        }
    }

    [GeneratedRegex(@"\.Map(Get|Post|Put|Delete|Patch|Methods|Group)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex InlineEndpoint();
}
