using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Amazon.ECS;
using Amazon.EventBridge;
using Amazon.EventBridge.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.Ingestion.Application.Abstractions.Providers;
using Sentinel.Ingestion.Domain.Observations;

namespace Sentinel.Ingestion.Infrastructure.Providers;

/// <summary>
/// Reads changes to EventBridge rules as change observations of kind <c>route</c> (Phase E01, Stage 4). A rule's pattern
/// decides which events reach its targets, so a changed pattern is a change to what its target queue's consumer receives:
/// the change is the consumer's, the ECS service whose task definition names the queue, else the queue's own. EventBridge
/// keeps no history of a rule, so this provider remembers each pattern it saw: the first sight of a rule is its baseline,
/// and a pattern that differs from the last one seen is a change, timed when it was first seen, effective at once. The
/// deploy-blame rules ignore the kind; a version is the pattern's digest, and the patterns themselves are attributes.
/// </summary>
internal sealed class EventBridgeRouteProvider(IAmazonEventBridge events, IAmazonECS ecs, AwsProviderOptions options, TimeProvider time, ILogger<EventBridgeRouteProvider> logger) : IDeploymentProvider
{
    public const string ProviderName = "eventbridge";

    private readonly ConcurrentDictionary<string, string> _patterns = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RouteChange> _changes = new(StringComparer.Ordinal);

    public string Name => ProviderName;

    public async Task<ProviderResult<DeploymentObservation>> GetDeploymentsAsync(DeploymentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            DateTimeOffset now = time.GetUtcNow();
            foreach (string bus in options.EventBuses)
            {
                foreach (Rule rule in await RulesAsync(bus, cancellationToken))
                {
                    await ObserveAsync(bus, rule, now, cancellationToken);
                }
            }

            var observations = new List<DeploymentObservation>();
            foreach (RouteChange change in _changes.Values.Where(change => change.ChangedAt >= request.Range.From && change.ChangedAt < request.Range.To).OrderBy(change => change.ChangedAt))
            {
                observations.Add(Observation(change, "started", "in-progress", completedAt: null, request, now));
                observations.Add(Observation(change, "completed", "completed", change.ChangedAt, request, now));
            }

            return new ProviderResult<DeploymentObservation>(observations, request.Range.To, false, ProviderStatus.Healthy);
        }
        catch (Exception exception) when (exception is AmazonServiceException or AmazonClientException or HttpRequestException or TimeoutException || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Reading EventBridge rules failed");
            return ProviderResult<DeploymentObservation>.Unavailable(request.Range.From, exception.Message);
        }
    }

    private async Task ObserveAsync(string bus, Rule rule, DateTimeOffset now, CancellationToken cancellationToken)
    {
        string pattern = rule.EventPattern ?? string.Empty;
        string key = $"{bus}/{rule.Name}";
        if (!_patterns.TryGetValue(key, out string? previous))
        {
            _patterns[key] = pattern;
            return;
        }

        if (previous == pattern)
        {
            return;
        }

        _patterns[key] = pattern;
        string? queue = await TargetQueueAsync(bus, rule.Name, cancellationToken);
        string owner = queue is null ? rule.Name : (await EcsQueueConsumers.FindAsync(ecs, options.Clusters, [queue], cancellationToken)).GetValueOrDefault(queue, queue);
        if (!PrometheusMetricsProvider.TryServiceId(owner, out ServiceId service))
        {
            logger.LogWarning("The EventBridge rule {Rule} on {Bus} changed, but {Owner} names no service; the change is not recorded", rule.Name, bus, owner);
            return;
        }

        var change = new RouteChange(
            string.Create(CultureInfo.InvariantCulture, $"{key}/{now.ToUnixTimeMilliseconds()}"),
            bus,
            rule.Name,
            queue,
            service,
            pattern,
            previous,
            now);
        _changes[change.Id] = change;
        logger.LogInformation("The EventBridge rule {Rule} on {Bus} changed its pattern from {Previous} to {Pattern}", rule.Name, bus, previous, pattern);
    }

    private async Task<List<Rule>> RulesAsync(string bus, CancellationToken cancellationToken)
    {
        var rules = new List<Rule>();
        string? next = null;
        do
        {
            ListRulesResponse page = await events.ListRulesAsync(new ListRulesRequest { EventBusName = bus, NextToken = next }, cancellationToken);
            rules.AddRange(page.Rules ?? []);
            next = page.NextToken;
        }
        while (!string.IsNullOrEmpty(next));

        return rules;
    }

    // The SQS queue a rule delivers to, by the last segment of its target's ARN.
    private async Task<string?> TargetQueueAsync(string bus, string rule, CancellationToken cancellationToken)
    {
        ListTargetsByRuleResponse targets = await events.ListTargetsByRuleAsync(new ListTargetsByRuleRequest { Rule = rule, EventBusName = bus }, cancellationToken);
        return (targets.Targets ?? []).Select(target => target.Arn).FirstOrDefault(arn => arn?.StartsWith("arn:aws:sqs:", StringComparison.Ordinal) == true)?.Split(':')[^1];
    }

    private static string Digest(string pattern) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pattern)))[..8];

    private static DeploymentObservation Observation(RouteChange change, string state, string status, DateTimeOffset? completedAt, DeploymentRequest request, DateTimeOffset ingestedAt) => new()
    {
        ObservationId = ObservationIdentity.Deployment(ProviderName, change.Id, state),
        TenantId = request.TenantId,
        EnvironmentId = request.EnvironmentId,
        ServiceId = change.Service,
        SourceProvider = ProviderName,
        SourceResource = $"events/{change.Bus}/rules/{change.Rule}",
        EventTime = change.ChangedAt,
        IngestTime = ingestedAt,
        DeploymentId = change.Id,
        Version = $"pattern {Digest(change.Pattern)}",
        PreviousVersion = $"pattern {Digest(change.PreviousPattern)}",
        Actor = ProviderName,
        StartedAt = change.ChangedAt,
        CompletedAt = completedAt,
        ChangeKind = "route",
        Status = status,
        Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rule"] = change.Rule,
            ["bus"] = change.Bus,
            ["queue"] = change.Queue ?? string.Empty,
            ["pattern"] = change.Pattern,
            ["previousPattern"] = change.PreviousPattern
        }
    };

    private sealed record RouteChange(string Id, string Bus, string Rule, string? Queue, ServiceId Service, string Pattern, string PreviousPattern, DateTimeOffset ChangedAt);
}
