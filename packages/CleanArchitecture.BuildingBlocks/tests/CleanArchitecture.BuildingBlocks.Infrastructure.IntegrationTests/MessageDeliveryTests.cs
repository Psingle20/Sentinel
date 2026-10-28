using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Infrastructure.PostgreSql;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using StackExchange.Redis;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.IntegrationTests;

[Collection(MessagingCollection.Name)]
public sealed class MessageDeliveryTests(MessagingInfrastructure infrastructure)
{
    private const string MessageType = "orders.order.placed";

    private sealed record Services(IHost Orders, string OrdersDb, IHost Billing, string BillingDb, string Stream) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Orders.StopAsync();
            await Billing.StopAsync();
            Orders.Dispose();
            Billing.Dispose();
        }
    }

    private async Task<Services> StartAsync(BillingBehavior? behavior = null)
    {
        string stream = $"it.orders.{Guid.NewGuid():N}";
        string ordersDb = await infrastructure.CreateDatabaseAsync("orders");
        string billingDb = await infrastructure.CreateDatabaseAsync("billing");
        await MessagingInfrastructure.ExecuteAsync(ordersDb, "create table orders (order_id uuid primary key)");
        await MessagingInfrastructure.ExecuteAsync(billingDb, CreateInvoiceOnOrderPlaced.CreateTable);

        IHost orders = await infrastructure.StartServiceAsync("orders", ordersDb, messages => messages.Produces<OrderPlaced>(MessageType, 1, stream));
        IHost billing = await infrastructure.StartServiceAsync(
            "billing",
            billingDb,
            messages => messages.Consumes<OrderPlaced, CreateInvoiceOnOrderPlaced>(MessageType, 1, stream),
            services => services.AddSingleton(behavior ?? new BillingBehavior()));

        return new Services(orders, ordersDb, billing, billingDb, stream);
    }

    /// <summary>Writes the order and enqueues its event in one unit of work, as an application handler would.</summary>
    private static async Task PlaceOrderAsync(IHost orders, Guid orderId, bool commit = true)
    {
        await using AsyncServiceScope scope = orders.Services.CreateAsyncScope();
        NpgsqlTransaction transaction = await scope.ServiceProvider.GetRequiredService<IPostgresTransaction>().GetTransactionAsync(CancellationToken.None);
        await using (var command = new NpgsqlCommand("insert into orders (order_id) values (@order_id)", transaction.Connection, transaction))
        {
            command.Parameters.AddWithValue("order_id", orderId);
            await command.ExecuteNonQueryAsync();
        }

        scope.ServiceProvider.GetRequiredService<IOutbox>().Enqueue(new OrderPlaced(orderId, 42.50m));

        if (commit)
        {
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().CommitAsync(CancellationToken.None);
        }
    }

    private Task<long> PendingAsync(string stream) =>
        infrastructure.Redis.GetDatabase().StreamGroupInfoAsync(stream)
            .ContinueWith(task => (long)task.Result.Single(group => group.Name == "billing").PendingMessageCount, TaskScheduler.Default);

    [Fact]
    public async Task A_message_crosses_services_once_and_a_redelivered_duplicate_is_suppressed()
    {
        await using Services services = await StartAsync();
        var orderId = Guid.NewGuid();

        await PlaceOrderAsync(services.Orders, orderId);

        await MessagingInfrastructure.EventuallyAsync(
            async () => await MessagingInfrastructure.ScalarAsync(services.BillingDb, "select count(*) from invoices") == 1,
            "billing creates the invoice");
        (await MessagingInfrastructure.ScalarAsync(services.OrdersDb, "select count(*) from messaging.outbox where published_at is not null")).ShouldBe(1);

        // Simulate at-least-once redelivery: the same envelope arrives again as a new stream entry.
        IDatabase redis = infrastructure.Redis.GetDatabase();
        StreamEntry original = (await redis.StreamRangeAsync(services.Stream)).ShouldHaveSingleItem();
        RedisValue duplicateId = await redis.StreamAddAsync(services.Stream, original.Values);

        await MessagingInfrastructure.EventuallyAsync(
            async () =>
            {
                StreamGroupInfo group = (await redis.StreamGroupInfoAsync(services.Stream)).Single(info => info.Name == "billing");
                return group.LastDeliveredId == duplicateId.ToString() && group.PendingMessageCount == 0;
            },
            "billing acknowledges the duplicate");

        (await MessagingInfrastructure.ScalarAsync(services.BillingDb, "select count(*) from invoices")).ShouldBe(1);
        (await MessagingInfrastructure.ScalarAsync(services.BillingDb, "select count(*) from messaging.inbox")).ShouldBe(1);
    }

    [Fact]
    public async Task A_rolled_back_unit_of_work_sends_nothing()
    {
        await using Services services = await StartAsync();

        await PlaceOrderAsync(services.Orders, Guid.NewGuid(), commit: false);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        (await MessagingInfrastructure.ScalarAsync(services.OrdersDb, "select count(*) from orders")).ShouldBe(0);
        (await MessagingInfrastructure.ScalarAsync(services.OrdersDb, "select count(*) from messaging.outbox")).ShouldBe(0);
        (await infrastructure.Redis.GetDatabase().StreamLengthAsync(services.Stream)).ShouldBe(0);
    }

    [Fact]
    public async Task A_failed_delivery_is_retried_after_the_claim_idle_time()
    {
        BillingBehavior behavior = new() { FailFirst = 2 };
        await using Services services = await StartAsync(behavior);

        await PlaceOrderAsync(services.Orders, Guid.NewGuid());

        await MessagingInfrastructure.EventuallyAsync(
            async () => await MessagingInfrastructure.ScalarAsync(services.BillingDb, "select count(*) from invoices") == 1,
            "billing succeeds on the third delivery");
        behavior.Attempts.ShouldBe(3);
        await MessagingInfrastructure.EventuallyAsync(async () => await PendingAsync(services.Stream) == 0, "the entry is acknowledged");
        (await infrastructure.Redis.GetDatabase().StreamLengthAsync(services.Stream + ".dlq")).ShouldBe(0);
    }

    [Fact]
    public async Task A_message_that_keeps_failing_is_dead_lettered_with_its_reason()
    {
        BillingBehavior behavior = new() { FailFirst = int.MaxValue };
        await using Services services = await StartAsync(behavior);

        await PlaceOrderAsync(services.Orders, Guid.NewGuid());

        IDatabase redis = infrastructure.Redis.GetDatabase();
        await MessagingInfrastructure.EventuallyAsync(async () => await redis.StreamLengthAsync(services.Stream + ".dlq") == 1, "the message is dead-lettered");

        StreamEntry deadLetter = (await redis.StreamRangeAsync(services.Stream + ".dlq")).ShouldHaveSingleItem();
        deadLetter["dlq.reason"].ToString().ShouldStartWith("Failed after 3 deliveries: Billing.Unavailable");
        behavior.Attempts.ShouldBe(3);
        (await PendingAsync(services.Stream)).ShouldBe(0);
        (await MessagingInfrastructure.ScalarAsync(services.BillingDb, "select count(*) from invoices")).ShouldBe(0);
    }

    [Fact]
    public async Task A_malformed_entry_is_dead_lettered_without_reaching_the_handler()
    {
        BillingBehavior behavior = new();
        await using Services services = await StartAsync(behavior);
        IDatabase redis = infrastructure.Redis.GetDatabase();

        await redis.StreamAddAsync(services.Stream, [new NameValueEntry("type", MessageType)]);

        await MessagingInfrastructure.EventuallyAsync(async () => await redis.StreamLengthAsync(services.Stream + ".dlq") == 1, "the entry is dead-lettered");
        (await redis.StreamRangeAsync(services.Stream + ".dlq")).ShouldHaveSingleItem()["dlq.reason"].ToString().ShouldStartWith("Malformed entry");
        behavior.Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task The_messaging_schema_can_be_created_concurrently_and_repeatedly()
    {
        string database = await infrastructure.CreateDatabaseAsync("schema");
        await using NpgsqlDataSource dataSource = NpgsqlDataSource.Create(database);
        var schema = new PostgresMessagingSchema(dataSource);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => schema.InitializeAsync(CancellationToken.None)));
        await schema.InitializeAsync(CancellationToken.None);

        (await MessagingInfrastructure.ScalarAsync(database, "select count(*) from information_schema.tables where table_schema = 'messaging'")).ShouldBe(2);
    }
}
