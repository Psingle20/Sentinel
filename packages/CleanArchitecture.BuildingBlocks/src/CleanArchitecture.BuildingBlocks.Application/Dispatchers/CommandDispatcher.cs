using System.Collections.Concurrent;
using System.Reflection;
using CleanArchitecture.BuildingBlocks.Messaging;

namespace CleanArchitecture.BuildingBlocks.Dispatchers;

internal sealed class CommandDispatcher(IServiceProvider serviceProvider) : ICommandDispatcher
{
    private static readonly ConcurrentDictionary<(Type CommandType, Type ResultType), HandlerInvoker> Invokers = new();

    public Task<Result<TResult>> Dispatch<TResult>(ICommand<TResult> command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        HandlerInvoker invoker = Invokers.GetOrAdd(
            (command.GetType(), typeof(TResult)),
            static key =>
            {
                Type handlerInterface = typeof(ICommandHandler<,>).MakeGenericType(key.CommandType, key.ResultType);
                MethodInfo handleMethod = handlerInterface.GetMethod(nameof(ICommandHandler<ICommand<object>, object>.Handle))!;
                return new HandlerInvoker(handlerInterface, handleMethod);
            });

        object handler = ResolveHandler(invoker, command);
        var task = (Task<Result<TResult>>)invoker.Invoke(handler, command, cancellationToken);
        return task;
    }

    public Task<Result> Dispatch(ICommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        HandlerInvoker invoker = Invokers.GetOrAdd(
            (command.GetType(), typeof(void)),
            static key =>
            {
                Type handlerInterface = typeof(ICommandHandler<>).MakeGenericType(key.CommandType);
                MethodInfo handleMethod = handlerInterface.GetMethod(nameof(ICommandHandler<ICommand>.Handle))!;
                return new HandlerInvoker(handlerInterface, handleMethod);
            });

        object handler = ResolveHandler(invoker, command);
        var task = (Task<Result>)invoker.Invoke(handler, command, cancellationToken);
        return task;
    }

    private object ResolveHandler(HandlerInvoker invoker, object command) =>
        serviceProvider.GetService(invoker.HandlerInterface)
        ?? throw new InvalidOperationException($"No handler is registered for command '{command.GetType().FullName}'.");

    private sealed record HandlerInvoker(Type HandlerInterface, MethodInfo HandleMethod)
    {
        // DoNotWrapExceptions preserves the handler's original exception instead of a TargetInvocationException.
        public object Invoke(object handler, object command, CancellationToken cancellationToken) =>
            HandleMethod.Invoke(handler, BindingFlags.DoNotWrapExceptions, binder: null, [command, cancellationToken], culture: null)!;
    }
}
