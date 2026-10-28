namespace CleanArchitecture.BuildingBlocks;

public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => [.. _domainEvents];

    /// <summary>
    /// Returns the pending domain events and clears them. Call this only from the
    /// persistence boundary that publishes the events after the entity is saved.
    /// </summary>
    public IReadOnlyList<IDomainEvent> DequeueDomainEvents()
    {
        IDomainEvent[] pending = [.. _domainEvents];
        _domainEvents.Clear();
        return pending;
    }

    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }
}
