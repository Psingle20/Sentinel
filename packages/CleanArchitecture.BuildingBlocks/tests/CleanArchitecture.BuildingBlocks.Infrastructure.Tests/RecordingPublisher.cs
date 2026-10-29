using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Integration;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Tests;

internal sealed class RecordingPublisher : IMessagePublisher
{
    public List<(string Stream, MessageEnvelope Envelope)> Sent { get; } = [];

    public Exception? FailWith { get; set; }

    public Task PublishAsync(string stream, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        if (FailWith is not null)
        {
            throw FailWith;
        }

        Sent.Add((stream, envelope));
        return Task.CompletedTask;
    }
}
