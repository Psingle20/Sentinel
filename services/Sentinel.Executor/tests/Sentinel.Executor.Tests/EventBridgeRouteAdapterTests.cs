using System.Text.Json.Nodes;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Infrastructure.Remediation;
using Shouldly;

namespace Sentinel.Executor.Tests;

/// <summary>
/// The EventBridge route adapter against local AWS (Phase E01, Stage 4): a drifted rule is restored to the
/// pattern Executor holds as approved and verified intact, restoring it again changes nothing, and the restoration is not
/// compensated. A rule without an approved pattern, or a capability for another action, leaves the rule as it is.
/// </summary>
[Trait("Category", "Aws")]
public sealed class EventBridgeRouteAdapterTests(LocalSqs localSqs) : IClassFixture<LocalSqs>
{
    private const string Approved = """{"source":["reference.checkout"],"detail-type":["OrderPlaced"]}""";
    private const string Drifted = """{"source":["reference.storefront"],"detail-type":["OrderPlaced"]}""";

    [Fact]
    public async Task A_drifted_route_is_restored_to_its_approved_pattern_and_verified_intact()
    {
        using AmazonEventBridgeClient events = localSqs.Events();
        string rule = $"orders-route-{Guid.NewGuid().ToString("N")[..8]}";
        await events.PutRuleAsync(new PutRuleRequest { Name = rule, EventPattern = Drifted, State = RuleState.ENABLED });
        EventBridgeRouteAdapter adapter = Adapter(events, rule);
        RestoreRouteAction action = Restore(rule);

        (await adapter.InspectAsync(action, "key", CancellationToken.None)).ServingRevision.ShouldBe(EventBridgeRouteAdapter.Drifted);
        AdapterResult result = await adapter.ExecuteAsync(action, CapabilityFor(adapter, action, "restore-route"), "key", CancellationToken.None);

        result.Outcome.ShouldBe(AdapterOutcome.Accepted, result.Detail);
        result.Detail.ShouldContain($"Restored rule {rule}");
        (await adapter.VerifyActionAsync(action, "key", CancellationToken.None)).ShouldBeTrue();
        JsonNode.DeepEquals(JsonNode.Parse((await events.DescribeRuleAsync(new DescribeRuleRequest { Name = rule })).EventPattern), JsonNode.Parse(Approved)).ShouldBeTrue("the rule routes its approved pattern");

        (await adapter.ExecuteAsync(action, CapabilityFor(adapter, action, "restore-route"), "key", CancellationToken.None)).Outcome.ShouldBe(AdapterOutcome.Accepted, "restoring an intact route changes nothing");
        (await adapter.CompensateAsync(action, CapabilityFor(adapter, action, "restore-route"), "key:compensate", CancellationToken.None)).Outcome.ShouldBe(AdapterOutcome.Rejected);
    }

    [Fact]
    public async Task A_rule_without_an_approved_pattern_or_a_capability_for_another_action_is_left_as_it_is()
    {
        using AmazonEventBridgeClient events = localSqs.Events();
        string rule = $"orders-route-{Guid.NewGuid().ToString("N")[..8]}";
        await events.PutRuleAsync(new PutRuleRequest { Name = rule, EventPattern = Drifted, State = RuleState.ENABLED });
        RestoreRouteAction action = Restore(rule);

        EventBridgeRouteAdapter unknown = Adapter(events, "payments-route");
        (await unknown.ExecuteAsync(action, CapabilityFor(unknown, action, "restore-route"), "key", CancellationToken.None)).Outcome.ShouldBe(AdapterOutcome.Rejected);
        EventBridgeRouteAdapter adapter = Adapter(events, rule);
        (await adapter.ExecuteAsync(action, CapabilityFor(adapter, action, "rollback"), "key", CancellationToken.None)).Outcome.ShouldBe(AdapterOutcome.Rejected);

        (await events.DescribeRuleAsync(new DescribeRuleRequest { Name = rule })).EventPattern.ShouldBe(Drifted, "nothing was put");
    }

    private EventBridgeRouteAdapter Adapter(AmazonEventBridgeClient events, string approvedRule) => new(
        events,
        new EventBridgeRouteOptions
        {
            ServiceUrl = localSqs.Endpoint,
            AccessKey = LocalSqs.Account,
            SecretKey = LocalSqs.Secret,
            Routes = new Dictionary<string, string>(StringComparer.Ordinal) { [approvedRule] = Approved }
        },
        TimeProvider.System);

    private static RestoreRouteAction Restore(string rule) => new()
    {
        ActionId = ActionId.New(),
        TargetServiceId = ServiceId.From("queue-consumer"),
        Rule = rule,
        EventBus = "default",
        Reason = "Its route stopped delivering."
    };

    private static Capability CapabilityFor(EventBridgeRouteAdapter adapter, RestoreRouteAction action, string kind) => Capability.Issue(
        Guid.NewGuid(),
        kind,
        "queue-consumer",
        TenantId.From("acme"),
        EnvironmentId.From("reference"),
        adapter.CapabilityLimits(action),
        DateTimeOffset.UtcNow,
        TimeSpan.FromMinutes(1));
}
