using System.Diagnostics.CodeAnalysis;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

internal sealed record ConsumerRegistration(MessageRegistration Message, HandlerInvoker Invoke);
