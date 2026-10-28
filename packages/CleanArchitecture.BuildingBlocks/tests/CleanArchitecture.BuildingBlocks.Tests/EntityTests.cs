using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Tests;

public sealed class EntityTests
{
    private sealed record OrderPlaced(int Number) : IDomainEvent;

    private sealed class Order : Entity
    {
        public void Place(int number) => Raise(new OrderPlaced(number));

        public void RaiseNothing() => Raise(null!);
    }

    [Fact]
    public void Domain_events_are_a_snapshot_of_raised_events()
    {
        Order order = new();
        order.Place(1);

        IReadOnlyCollection<IDomainEvent> snapshot = order.DomainEvents;
        order.Place(2);

        snapshot.ShouldBe([new OrderPlaced(1)]);
        order.DomainEvents.ShouldBe([new OrderPlaced(1), new OrderPlaced(2)]);
    }

    [Fact]
    public void Dequeue_returns_events_in_order_and_clears_them()
    {
        Order order = new();
        order.Place(1);
        order.Place(2);

        order.DequeueDomainEvents().ShouldBe([new OrderPlaced(1), new OrderPlaced(2)]);
        order.DomainEvents.ShouldBeEmpty();
        order.DequeueDomainEvents().ShouldBeEmpty();
    }

    [Fact]
    public void Raising_a_null_event_is_rejected()
    {
        Should.Throw<ArgumentNullException>(() => new Order().RaiseNothing());
    }
}
