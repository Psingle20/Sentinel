using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Tests;

public sealed class RegistrationTests
{
    private sealed record OrderCancelled(Guid OrderId);

    [Fact]
    public void Registering_the_same_mapping_twice_is_allowed()
    {
        MessagingBuilder builder = new ServiceCollection().AddMessaging("orders");

        builder.Produces<OrderPlaced>("orders.order.placed", 1, "test.orders");
        builder.Produces<OrderPlaced>("orders.order.placed", 1, "test.orders");

        builder.Registry.Get(typeof(OrderPlaced)).MessageType.ShouldBe("orders.order.placed");
    }

    [Fact]
    public void One_type_cannot_map_to_two_names()
    {
        MessagingBuilder builder = new ServiceCollection().AddMessaging("orders").Produces<OrderPlaced>("orders.order.placed", 1, "test.orders");

        Should.Throw<InvalidOperationException>(() => builder.Produces<OrderPlaced>("orders.order.created", 1, "test.orders"));
    }

    [Fact]
    public void Two_types_cannot_share_a_name_and_version()
    {
        MessagingBuilder builder = new ServiceCollection().AddMessaging("orders").Produces<OrderPlaced>("orders.order.placed", 1, "test.orders");

        Should.Throw<InvalidOperationException>(() => builder.Produces<OrderCancelled>("orders.order.placed", 1, "test.orders"));
    }

    [Fact]
    public void A_message_type_can_have_only_one_handler()
    {
        MessagingBuilder builder = new ServiceCollection().AddMessaging("billing")
            .Consumes<OrderPlaced, RecordingHandler>("orders.order.placed", 1, "test.orders");

        Should.Throw<InvalidOperationException>(() => builder.Consumes<OrderPlaced, RecordingHandler>("orders.order.placed", 1, "test.orders"));
    }

    [Fact]
    public void Adding_messaging_twice_is_rejected()
    {
        ServiceCollection services = new();
        services.AddMessaging("orders");

        Should.Throw<InvalidOperationException>(() => services.AddMessaging("orders"));
    }

    [Theory]
    [InlineData("", 1, "stream")]
    [InlineData("orders.order.placed", 0, "stream")]
    [InlineData("orders.order.placed", 1, " ")]
    public void Invalid_registrations_are_rejected(string messageType, int version, string stream)
    {
        MessagingBuilder builder = new ServiceCollection().AddMessaging("orders");

        Should.Throw<ArgumentException>(() => builder.Produces<OrderPlaced>(messageType, version, stream));
    }
}
