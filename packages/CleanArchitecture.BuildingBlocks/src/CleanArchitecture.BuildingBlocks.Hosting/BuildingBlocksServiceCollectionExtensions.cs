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
        typeof(IQueryHandler<,>),
        typeof(IProcessor<>),
        typeof(IProcessor<,>)
    ];

    /// <summary>
    /// Registers the dispatchers, every command and query handler, every request processor and every
    /// validator in <paramref name="assemblies"/>. Handlers are wrapped in a fixed pipeline, logging
    /// outside and validation inside; processors are wrapped in request validation, so the request's
    /// validators run once, where untrusted input becomes an application message (ADR-020). Call once
    /// per service collection, from the service's Application dependency module.
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
        services.AddScoped<IRequestDispatcher, RequestDispatcher>();

        RegisterHandlers(services, types);
        new AssemblyScanner(types).ForEach(result => services.AddScoped(result.InterfaceType, result.ValidatorType));

        // Decoration order is fixed: each call wraps the previous one, so validation runs inside logging.
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(ValidationDecorator.CommandHandler<,>));
        services.TryDecorate(typeof(ICommandHandler<>), typeof(ValidationDecorator.CommandBaseHandler<>));
        services.TryDecorate(typeof(ICommandHandler<,>), typeof(LoggingDecorator.CommandHandler<,>));
        services.TryDecorate(typeof(ICommandHandler<>), typeof(LoggingDecorator.CommandBaseHandler<>));
        services.TryDecorate(typeof(IQueryHandler<,>), typeof(LoggingDecorator.QueryHandler<,>));
        services.TryDecorate(typeof(IProcessor<,>), typeof(ValidationDecorator.Processor<,>));
        services.TryDecorate(typeof(IProcessor<>), typeof(ValidationDecorator.ProcessorBase<>));

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
                $"Each command and query must have exactly one handler, and each request exactly one processor. Duplicates: {string.Join(", ", duplicates)}.");
        }
    }

    private static bool IsHandlerInterface(Type type) =>
        type.IsGenericType && HandlerInterfaces.Contains(type.GetGenericTypeDefinition());

    private sealed class RegistrationMarker;
}
