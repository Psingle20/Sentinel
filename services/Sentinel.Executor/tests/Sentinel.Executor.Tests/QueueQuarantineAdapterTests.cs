using Microsoft.Extensions.Time.Testing;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Infrastructure.Remediation;
using Shouldly;

namespace Sentinel.Executor.Tests;

/// <summary>
/// The quarantine adapter of Phase E01 against the reference queue consumer's endpoints: it sees the queue as poisoned
/// while a message reaches the action's receive count, moves only such messages with the capability's limits, verifies
/// that none remains, and releases them as its compensation.
/// </summary>
public sealed class QueueQuarantineAdapterTests
{
    private static readonly FakeTimeProvider Time = new(new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero));

    private static readonly QuarantineMessageAction Quarantine = new()
    {
        ActionId = ActionId.New(),
        TargetServiceId = ServiceId.From("queue-consumer"),
        Queue = "orders",
        MinimumReceives = 3,
        MaxMessages = 5,
        Reason = "It fails on every receive."
    };

    [Fact]
    public async Task A_poisoned_queue_is_quarantined_within_the_actions_limits_and_verified_clear()
    {
        var consumer = new FakeQueueConsumer();
        ReferenceQueueQuarantineAdapter adapter = Adapter(consumer);

        TargetInspection before = await adapter.InspectAsync(Quarantine, "key", CancellationToken.None);
        before.ServingRevision.ShouldBe(ReferenceQueueQuarantineAdapter.Poisoned);
        before.Detail.ShouldContain("ord-poison-1 x12");

        AdapterResult result = await adapter.ExecuteAsync(Quarantine, CapabilityFor(adapter, "quarantine-message"), "key", CancellationToken.None);

        result.Outcome.ShouldBe(AdapterOutcome.Accepted);
        result.Detail.ShouldContain("ord-poison-1");
        (string path, string? token, System.Text.Json.Nodes.JsonNode? body) = consumer.Controls.ShouldHaveSingleItem();
        path.ShouldBe("/_reference/queue/quarantine");
        token.ShouldBe("control-test-not-a-token");
        body!["minimumReceives"]!.GetValue<int>().ShouldBe(3);
        body["maxMessages"]!.GetValue<int>().ShouldBe(5);
        (await adapter.VerifyActionAsync(Quarantine, "key", CancellationToken.None)).ShouldBeTrue();
        (await adapter.InspectAsync(Quarantine, "key", CancellationToken.None)).Operation.ShouldBe(OperationStatus.Completed);
    }

    [Fact]
    public async Task A_message_below_the_receive_count_is_no_poison_and_a_capability_for_another_action_moves_nothing()
    {
        var consumer = new FakeQueueConsumer { Receives = 2 };
        ReferenceQueueQuarantineAdapter adapter = Adapter(consumer);

        (await adapter.InspectAsync(Quarantine, "key", CancellationToken.None)).ServingRevision.ShouldBe(ReferenceQueueQuarantineAdapter.Clear);
        AdapterResult refused = await adapter.ExecuteAsync(Quarantine, CapabilityFor(adapter, "rollback"), "key", CancellationToken.None);

        refused.Outcome.ShouldBe(AdapterOutcome.Rejected);
        consumer.Controls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Compensation_releases_the_quarantined_messages()
    {
        var consumer = new FakeQueueConsumer();
        ReferenceQueueQuarantineAdapter adapter = Adapter(consumer);
        await adapter.ExecuteAsync(Quarantine, CapabilityFor(adapter, "quarantine-message"), "key", CancellationToken.None);

        AdapterResult released = await adapter.CompensateAsync(Quarantine, CapabilityFor(adapter, "quarantine-message"), "key:compensate", CancellationToken.None);

        released.Outcome.ShouldBe(AdapterOutcome.Accepted);
        consumer.Controls[^1].Path.ShouldBe("/_reference/queue/release");
        (await adapter.InspectAsync(Quarantine, "key", CancellationToken.None)).ServingRevision.ShouldBe(ReferenceQueueQuarantineAdapter.Poisoned);
    }

    private static ReferenceQueueQuarantineAdapter Adapter(FakeQueueConsumer consumer)
    {
        var http = new HttpClient(consumer) { BaseAddress = new Uri("http://queue-consumer.test") };
        http.DefaultRequestHeaders.Add(ReferenceQueueQuarantineAdapter.ControlHeader, "control-test-not-a-token");
        return new ReferenceQueueQuarantineAdapter(http, Time);
    }

    private static Capability CapabilityFor(ReferenceQueueQuarantineAdapter adapter, string action) => Capability.Issue(
        Guid.NewGuid(),
        action,
        "queue-consumer",
        TenantId.From("acme"),
        EnvironmentId.From("reference"),
        adapter.CapabilityLimits(Quarantine),
        Time.GetUtcNow(),
        TimeSpan.FromMinutes(1));
}
