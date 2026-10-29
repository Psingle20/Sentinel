using System.Reflection;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Sentinel.Testing.Composition;

/// <summary>
/// The composition rules of the application composition guide (ADR-020), checked against a host's
/// registrations. Each method returns the violations it found; an empty list means the rule holds.
/// </summary>
public static class CompositionAssertions
{
    private static readonly (Type Message, Type Handler)[] HandlerShapes =
    [
        (typeof(ICommand), typeof(ICommandHandler<>)),
        (typeof(ICommand<>), typeof(ICommandHandler<,>)),
        (typeof(IQuery<>), typeof(IQueryHandler<,>)),
        (typeof(IRequest), typeof(IProcessor<>)),
        (typeof(IRequest<>), typeof(IProcessor<,>))
    ];

    /// <summary>Every command, query and request in <paramref name="application"/> has exactly one handler or processor, and it resolves.</summary>
    public static async Task<IReadOnlyList<string>> HandlerProblemsAsync(IServiceCollection services, IServiceProvider provider, params Assembly[] application)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(provider);

        var problems = new List<string>();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        foreach (Type message in ConcreteTypes(application))
        {
            foreach (Type handler in HandlersOf(message))
            {
                int registered = services.Count(descriptor => Registers(descriptor, handler));
                if (registered != 1)
                {
                    problems.Add($"{message.FullName} has {registered} registrations of {handler.Name}; exactly one is required.");
                    continue;
                }

                try
                {
                    _ = scope.ServiceProvider.GetRequiredService(handler);
                }
                catch (InvalidOperationException exception)
                {
                    problems.Add($"{message.FullName}'s {handler.Name} does not resolve: {exception.Message}");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// Every port, an interface <paramref name="application"/> declares and no application type
    /// implements, has exactly one registered implementation. <paramref name="collections"/> lists
    /// the ports that are deliberately implemented several times and consumed as a collection.
    /// </summary>
    public static IReadOnlyList<string> PortProblems(IServiceCollection services, IReadOnlyCollection<Type> collections, params Assembly[] application)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(collections);

        Type[] types = [.. application.SelectMany(assembly => assembly.GetTypes())];
        IEnumerable<Type> ports = types
            .Where(type => type.IsInterface && !type.IsGenericTypeDefinition)
            .Where(port => !types.Any(type => type is { IsClass: true, IsAbstract: false } && port.IsAssignableFrom(type)));

        var problems = new List<string>();
        foreach (Type port in ports)
        {
            int registered = services.Count(descriptor => Registers(descriptor, port));
            bool collection = collections.Contains(port);
            if (collection ? registered == 0 : registered != 1)
            {
                problems.Add($"Port {port.FullName} has {registered} registered implementations; {(collection ? "at least one" : "exactly one")} is required.");
            }
        }

        return problems;
    }

    /// <summary>
    /// No singleton captures a scoped service. The container's own validation walks every
    /// type-based registration; a factory registration is opaque to it and is checked when first
    /// resolved in Development, where scope validation is on.
    /// </summary>
    public static IReadOnlyList<string> LifetimeProblems(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        IServiceCollection copy = new ServiceCollection();
        foreach (ServiceDescriptor descriptor in services)
        {
            copy.Add(descriptor);
        }

        try
        {
            using ServiceProvider _ = copy.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            return [];
        }
        catch (AggregateException exception)
        {
            return [.. exception.InnerExceptions.Select(inner => inner.Message).Where(message => message.Contains("scoped", StringComparison.OrdinalIgnoreCase))];
        }
    }

    /// <summary>No hosted service (which includes the outbox dispatcher and the stream consumers) and no message consumer is registered twice.</summary>
    public static IReadOnlyList<string> DuplicateProblems(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        IEnumerable<string> hosted = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(ImplementationOf)
            .Where(type => type is not null && type != typeof(IHostedService) && type != typeof(object))
            .GroupBy(type => type!)
            .Where(group => group.Count() > 1)
            .Select(group => $"Hosted service {group.Key.FullName} is registered {group.Count()} times.");

        IEnumerable<string> consumers = services
            .Where(descriptor => descriptor.ServiceType.IsGenericType && descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IIntegrationMessageHandler<>))
            .GroupBy(descriptor => descriptor.ServiceType)
            .Where(group => group.Count() > 1)
            .Select(group => $"Message {group.Key.GetGenericArguments()[0].FullName} has {group.Count()} consumers.");

        return [.. hosted, .. consumers];
    }

    /// <summary>
    /// A layer's module registers only what that layer owns (ADR-020): no implementation from
    /// <paramref name="otherLayers"/>, whether registered by type or returned by a factory. Port
    /// interfaces from inner layers may be the service type, and a factory typed as the port says
    /// nothing about its implementation; implementations from another layer may not be registered.
    /// <paramref name="pending"/> names, by full name, implementations a later refactoring step moves;
    /// an entry the module no longer registers is reported, so the list can only shrink.
    /// </summary>
    public static IReadOnlyList<string> ModuleProblems(IDependencyModule module, IConfiguration configuration, IReadOnlyCollection<string> pending, params Assembly[] otherLayers)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(pending);

        var services = new ServiceCollection();
        module.RegisterServices(services, configuration);
        string[] foreign = [.. services
            .Select(ImplementationOf)
            .Where(type => type is { IsInterface: false, IsAbstract: false } && otherLayers.Contains(type.Assembly))
            .Select(type => type!.FullName ?? type.Name)
            .Distinct()];

        string name = module.GetType().Name;
        return [
            .. foreign.Where(type => !pending.Contains(type)).Select(type => $"{name} registers {type}, which another layer owns."),
            .. pending.Where(type => !foreign.Contains(type)).Select(type => $"Pending entry {type} is no longer registered by {name}; remove it from the pending list.")
        ];
    }

    // A decorator library keeps each decorated registration under a delegating type that compares equal to the service type,
    // and keyed registrations are a different service; neither is a second implementation.
    private static bool Registers(ServiceDescriptor descriptor, Type serviceType) =>
        descriptor.ServiceType == serviceType && descriptor.ServiceType is not TypeDelegator && descriptor.ServiceKey is null;

    private static Type? ImplementationOf(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType
        ?? descriptor.ImplementationInstance?.GetType()
        ?? descriptor.ImplementationFactory?.Method.ReturnType;

    private static IEnumerable<Type> ConcreteTypes(Assembly[] assemblies) =>
        assemblies.SelectMany(assembly => assembly.GetTypes()).Where(type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false });

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
}
