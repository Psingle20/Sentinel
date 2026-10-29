using System.Diagnostics.CodeAnalysis;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

internal delegate Task<Result> HandlerInvoker(
    IServiceProvider services,
    object message,
    MessageEnvelope envelope,
    CancellationToken cancellationToken);
