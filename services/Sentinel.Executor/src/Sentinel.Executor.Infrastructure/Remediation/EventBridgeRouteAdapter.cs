using System.Text.Json.Nodes;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Amazon.Runtime;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Domain.Capabilities;

namespace Sentinel.Executor.Infrastructure.Remediation;

/// <summary>
/// Restores an EventBridge rule to its approved pattern (Phase E01, Stage 4), so the events a drifted pattern stopped
/// matching reach their target queue again. The pattern is never the action's: Executor holds the approved pattern of each
/// rule it may restore, and a rule without one is refused. A route is <c>intact</c> while its pattern is the approved one
/// and <c>drifted</c> otherwise. Putting the approved pattern again is idempotent, so a repeated execution changes nothing;
/// there is no automatic compensation, because drifting the route again is a human decision.
/// </summary>
internal sealed class EventBridgeRouteAdapter(IAmazonEventBridge events, EventBridgeRouteOptions options, TimeProvider time) : IRemediationAdapter
{
    public const string Intact = "intact";
    public const string Drifted = "drifted";

    public string ActionKind => "restore-route";

    public IReadOnlyDictionary<string, string> CapabilityLimits(RemediationAction action)
    {
        RestoreRouteAction restore = Expect(action);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["operation"] = "restore-route",
            ["rule"] = restore.Rule,
            ["bus"] = restore.EventBus
        };
    }

    public async Task<TargetInspection> InspectAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken)
    {
        RestoreRouteAction restore = Expect(action);
        if (!options.Routes.TryGetValue(restore.Rule, out string? approved))
        {
            return new TargetInspection(null, OperationStatus.NotFound, $"Rule {restore.Rule} has no approved pattern in Executor.");
        }

        try
        {
            string? pattern = (await events.DescribeRuleAsync(new DescribeRuleRequest { Name = restore.Rule, EventBusName = restore.EventBus }, cancellationToken)).EventPattern;
            bool intact = Same(pattern, approved);
            return new TargetInspection(
                intact ? Intact : Drifted,
                intact ? OperationStatus.Completed : OperationStatus.NotFound,
                intact ? $"Rule {restore.Rule} routes its approved pattern." : $"Rule {restore.Rule} routes {pattern}, not its approved pattern {approved}.");
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException or TimeoutException)
        {
            return new TargetInspection(null, OperationStatus.Failed, $"Inspecting {restore.Rule} failed: {exception.Message}");
        }
    }

    public async Task<AdapterResult> ExecuteAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken)
    {
        RestoreRouteAction restore = Expect(action);
        ArgumentNullException.ThrowIfNull(capability);

        if (CapabilityCheck.Refuse(capability, ActionKind, action.TargetServiceId.Value, time) is { } refused)
        {
            return refused;
        }

        if (!options.Routes.TryGetValue(restore.Rule, out string? approved))
        {
            return new AdapterResult(AdapterOutcome.Rejected, $"Rule {restore.Rule} has no approved pattern in Executor.");
        }

        try
        {
            await events.PutRuleAsync(new PutRuleRequest { Name = restore.Rule, EventBusName = restore.EventBus, EventPattern = approved, State = RuleState.ENABLED }, cancellationToken);
            string? pattern = (await events.DescribeRuleAsync(new DescribeRuleRequest { Name = restore.Rule, EventBusName = restore.EventBus }, cancellationToken)).EventPattern;
            return Same(pattern, approved)
                ? new AdapterResult(AdapterOutcome.Accepted, $"Restored rule {restore.Rule} on {restore.EventBus} to its approved pattern {approved}.")
                : new AdapterResult(AdapterOutcome.Unknown, $"Rule {restore.Rule} reports {pattern} after the approved pattern was put.");
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException or TimeoutException)
        {
            return new AdapterResult(AdapterOutcome.Unknown, $"Restoring {restore.Rule} failed: {exception.Message}");
        }
    }

    /// <summary>True once the rule routes its approved pattern.</summary>
    public async Task<bool> VerifyActionAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken) =>
        (await InspectAsync(action, idempotencyKey, cancellationToken)).ServingRevision == Intact;

    public Task<AdapterResult> CompensateAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(new AdapterResult(AdapterOutcome.Rejected, "A restored route is not compensated automatically: drifting it again is a human decision."));

    // Patterns are compared as JSON, since a service may reformat the text of a pattern it was given.
    private static bool Same(string? pattern, string approved) =>
        pattern is not null && JsonNode.DeepEquals(JsonNode.Parse(pattern), JsonNode.Parse(approved));

    private static RestoreRouteAction Expect(RemediationAction action) =>
        action as RestoreRouteAction ?? throw new ArgumentException($"Expected a restore-route action, not {action?.GetType().Name}.", nameof(action));
}
