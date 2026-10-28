using System.Reflection;
using CleanArchitecture.BuildingBlocks.Behaviors;
using CleanArchitecture.BuildingBlocks.Dispatchers;
using CleanArchitecture.BuildingBlocks.Messaging;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.BuildingBlocks;

public static class BuildingBlocksServiceCollectionExtensions
{
    private static readonly Type[] HandlerInterfaces =
    [
        typeof(ICommandHandler<>),
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>)
    ];

    /// <summary>
    /// Registers the dispatchers, every command and query handler and every validator in
    /// <paramref name="assemblies"/>, and wraps handlers in a fixed pipeline: logging outside,
    /// validation inside. Call once per service collection.
    /// </summary>
    public static IServiceCollection AddBuildingBlocks(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        return services.AddBuildingBlocks(assemblies.SelectMany(assembly => assembly.GetTypes()));
    }

    internal static IServiceCollection AddBuildingBlocks(
        this IServiceCollection services,
        IEnumerable<Type> candidateTypes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(candidateTypes);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(RegistrationMarker)))
        {
            throw new InvalidOperationException("AddBuildingBlocks has already been called for this service collection.");
        }

        Type[] types = [.. candidateTypes.Where(type => type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })];

        services.AddSingleton<RegistrationMarker>();
        services.AddScoped<ICommandDispatcher, CommandDispatcher>();
        services.AddScoped<IQueryDispatcher, QueryDispatcher>();

        RegisterHandlers(services, types);
        new AssemblyScanner(types).ForEach(result => services.AddScoped(result.InterfaceType, result.ValidatorType));

        // Decoration order is fixed: each call wraps the previous one, so validation runs inside logging.
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator.CommandHandler<,>));
        services.TryDecorate(typeof(ICommandHandler<>), typeof(ValidationDecorator.CommandBaseHandler<>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(LoggingDecorator.CommandHandler<,>));
        services.TryDecorate(typeof(ICommandHandler<>), typeof(LoggingDecorator.CommandBaseHandler<>));
        services.TryDecorate(typeof(IQueryHandler<,>), typeof(LoggingDecorator.QueryHandler<,>));

        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Type[] types)
    {
        foreach (Type implementation in types)
        {
            foreach (Type handlerInterface in implementation.GetInterfaces().Where(IsHandlerInterface))
            {
                services.AddScoped(handlerInterface, implementation);
            }
        }

        string[] duplicates = services
            .Where(descriptor => IsHandlerInterface(descriptor.ServiceType))
            .GroupBy(descriptor => descriptor.ServiceType)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.ToString())
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException(
                $"Each command and query must have exactly one handler. Duplicates: {string.Join(", ", duplicates)}.");
        }
    }

    private static bool IsHandlerInterface(Type type) =>
        type.IsGenericType && HandlerInterfaces.Contains(type.GetGenericTypeDefinition());

    private sealed class RegistrationMarker;
}
