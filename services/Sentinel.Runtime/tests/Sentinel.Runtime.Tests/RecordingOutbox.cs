using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Tests;

/// <summary>Captures enqueued messages instead of storing them.</summary>
internal sealed class RecordingOutbox : IOutbox
{
    public List<(object Message, MessageMetadata? Metadata)> Messages { get; } = [];

    public void Enqueue<TMessage>(TMessage message, MessageMetadata? metadata = null)
        where TMessage : class => Messages.Add((message, metadata));

    public IEnumerable<TMessage> Of<TMessage>() => Messages.Select(entry => entry.Message).OfType<TMessage>();
}
