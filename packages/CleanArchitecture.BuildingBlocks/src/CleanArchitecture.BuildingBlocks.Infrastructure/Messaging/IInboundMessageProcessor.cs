using System.Diagnostics;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Handles one delivered envelope. Transports call this and act on the outcome.</summary>
public interface IInboundMessageProcessor
{
    Task<ProcessingResult> ProcessAsync(MessageEnvelope envelope, CancellationToken cancellationToken);
}
