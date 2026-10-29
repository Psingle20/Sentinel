using System.Collections.Concurrent;
using System.Reflection;
using CleanArchitecture.BuildingBlocks.Messaging;

namespace CleanArchitecture.BuildingBlocks.Dispatchers;

internal sealed class RequestDispatcher(IServiceProvider serviceProvider) : IRequestDispatcher
{
    private static readonly ConcurrentDictionary<(Type RequestType, Type ResultType), ProcessorInvoker> Invokers = new();

    public Task<Result<TResult>> SendAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ProcessorInvoker invoker = Invokers.GetOrAdd(
            (request.GetType(), typeof(TResult)),
            static key =>
            {
                Type processorInterface = typeof(IProcessor<,>).MakeGenericType(key.RequestType, key.ResultType);
                MethodInfo processMethod = processorInterface.GetMethod(nameof(IProcessor<IRequest<object>, object>.Process))!;
                return new ProcessorInvoker(processorInterface, processMethod);
            });

        object processor = ResolveProcessor(invoker, request);
        return (Task<Result<TResult>>)invoker.Invoke(processor, request, cancellationToken);
    }

    public Task<Result> SendAsync(IRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ProcessorInvoker invoker = Invokers.GetOrAdd(
            (request.GetType(), typeof(void)),
            static key =>
            {
                Type processorInterface = typeof(IProcessor<>).MakeGenericType(key.RequestType);
                MethodInfo processMethod = processorInterface.GetMethod(nameof(IProcessor<IRequest>.Process))!;
                return new ProcessorInvoker(processorInterface, processMethod);
            });

        object processor = ResolveProcessor(invoker, request);
        return (Task<Result>)invoker.Invoke(processor, request, cancellationToken);
    }

    private object ResolveProcessor(ProcessorInvoker invoker, object request) =>
        serviceProvider.GetService(invoker.ProcessorInterface)
        ?? throw new InvalidOperationException($"No processor is registered for request '{request.GetType().FullName}'.");

    private sealed record ProcessorInvoker(Type ProcessorInterface, MethodInfo ProcessMethod)
    {
        // DoNotWrapExceptions preserves the processor's original exception instead of a TargetInvocationException.
        public object Invoke(object processor, object request, CancellationToken cancellationToken) =>
            ProcessMethod.Invoke(processor, BindingFlags.DoNotWrapExceptions, binder: null, [request, cancellationToken], culture: null)!;
    }
}
