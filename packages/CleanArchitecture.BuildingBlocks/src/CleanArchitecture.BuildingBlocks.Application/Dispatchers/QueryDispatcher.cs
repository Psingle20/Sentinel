using System.Collections.Concurrent;
using System.Reflection;
using CleanArchitecture.BuildingBlocks.Messaging;

namespace CleanArchitecture.BuildingBlocks.Dispatchers;

internal sealed class QueryDispatcher(IServiceProvider serviceProvider) : IQueryDispatcher
{
    private static readonly ConcurrentDictionary<(Type QueryType, Type ResultType), HandlerInvoker> Invokers = new();

    public Task<Result<TResult>> Dispatch<TResult>(IQuery<TResult> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        HandlerInvoker invoker = Invokers.GetOrAdd(
            (query.GetType(), typeof(TResult)),
            static key =>
            {
                Type handlerInterface = typeof(IQueryHandler<,>).MakeGenericType(key.QueryType, key.ResultType);
                MethodInfo handleMethod = handlerInterface.GetMethod(nameof(IQueryHandler<IQuery<object>, object>.Handle))!;
                return new HandlerInvoker(handlerInterface, handleMethod);
            });

        object handler = serviceProvider.GetService(invoker.HandlerInterface)
            ?? throw new InvalidOperationException($"No handler is registered for query '{query.GetType().FullName}'.");
        var task = (Task<Result<TResult>>)invoker.Invoke(handler, query, cancellationToken);
        return task;
    }

    private sealed record HandlerInvoker(Type HandlerInterface, MethodInfo HandleMethod)
    {
        // DoNotWrapExceptions preserves the handler's original exception instead of a TargetInvocationException.
        public object Invoke(object handler, object query, CancellationToken cancellationToken) =>
            HandleMethod.Invoke(handler, BindingFlags.DoNotWrapExceptions, binder: null, [query, cancellationToken], culture: null)!;
    }
}
