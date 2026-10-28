namespace CleanArchitecture.BuildingBlocks.Integration;

/// <summary>
/// Enqueues integration messages in the current unit of work. Messages are stored when the
/// unit of work commits and published afterwards, so a message exists only if its cause does.
/// </summary>
public interface IOutbox
{
    void Enqueue<TMessage>(TMessage message, MessageMetadata? metadata = null)
        where TMessage : class;
}
