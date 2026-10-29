using System.Reflection;
using System.Text.RegularExpressions;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using FluentValidation;
using NetArchTest.Rules;

namespace Sentinel.Testing.Architecture;

/// <summary>
/// Rules 6 to 10 of the application composition guide's enforceable rules (ADR-020), shared by every
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

    private static readonly Type[] ProcessorDependencies = [typeof(ICommandDispatcher), typeof(IQueryDispatcher)];

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
                .Where(parameter => !ProcessorDependencies.Contains(parameter) && !IsValidator(parameter))
                .Select(parameter => $"Processor {processor.FullName} depends on {parameter.Name}; a processor depends only on a validator and a dispatcher."))];
    }

    /// <summary>Rule 8: message consumers live in <c>Infrastructure.Messaging.Consumers</c> and depend only on <see cref="IRequestDispatcher"/>.</summary>
    public static IReadOnlyList<string> Consumers(Assembly infrastructure, string consumersNamespace, IReadOnlyCollection<string> pending)
    {
        ArgumentNullException.ThrowIfNull(infrastructure);
        ArgumentNullException.ThrowIfNull(pending);

        Type[] consumers = [.. Concrete(infrastructure).Where(type => type.GetInterfaces().Any(IsConsumer))];
        return Judge(consumers.Select(consumer => (consumer, ConsumerProblems(consumer, consumersNamespace))), pending);
    }

    /// <summary>
    /// Rule 9: controllers derive from the service's <c>ApiController</c>, depend on
    /// <see cref="IRequestDispatcher"/> and on nothing from <paramref name="forbiddenNamespaces"/>, and
    /// <c>Program.cs</c> maps no endpoint itself.
    /// </summary>
    public static IReadOnlyList<string> Controllers(Assembly api, string controllersNamespace, Type apiController, IReadOnlyCollection<string> forbiddenNamespaces, IReadOnlyCollection<string> pending)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(apiController);
        ArgumentNullException.ThrowIfNull(forbiddenNamespaces);

        Type[] controllers = [.. Concrete(api).Where(type => type.Namespace == controllersNamespace && type != apiController)];
        return Judge(controllers.Select(controller => (controller, ControllerProblems(controller, apiController, forbiddenNamespaces))), pending);
    }

    /// <summary>Rule 9, second half: the host's <c>Program.cs</c> is composition and middleware, with no inline endpoint.</summary>
    public static IReadOnlyList<string> ProgramEndpoints(string programSource)
    {
        ArgumentNullException.ThrowIfNull(programSource);

        return [.. InlineEndpoint().Matches(File.ReadAllText(programSource))
            .Select(match => $"{Path.GetFileName(programSource)} maps an endpoint inline ({match.Value.Trim()}); it belongs on a controller.")];
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

    /// <summary>The source file of a project, found by walking up from the test's output directory.</summary>
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

    private static IReadOnlyList<string> ConsumerProblems(Type consumer, string consumersNamespace)
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

    private static IReadOnlyList<string> ControllerProblems(Type controller, Type apiController, IReadOnlyCollection<string> forbiddenNamespaces)
    {
        var problems = new List<string>();
        if (!controller.IsSubclassOf(apiController))
        {
            problems.Add($"Controller {controller.FullName} does not derive from {apiController.Name}.");
        }

        Type[] parameters = Parameters(controller);
        if (!parameters.Contains(typeof(IRequestDispatcher)))
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
