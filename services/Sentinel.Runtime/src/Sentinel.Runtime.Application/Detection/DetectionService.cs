using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Correlation;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;

namespace Sentinel.Runtime.Application;

/// <summary>
/// Detect and correlate (Phase 03). Runs inside one correlation partition's turn (CorrelationGrain),
/// so decisions for a tenant and environment are serialized. A detection rule's firing signal is
/// handed to the correlation engine, which opens, merges, reopens, absorbs or suppresses with
/// explainable reasons; changes and observed dependency edges keep correlation state current.
/// Every path is idempotent: a redelivered observation changes nothing that was already applied.
/// </summary>
public sealed class DetectionService(
    ICorrelationStore correlation,
    IIncidentStore incidents,
    IIncidentOwner owner,
    IIncidentWorkflows workflows,
    IServiceCatalog catalog,
    ServiceTopologyCache topologies,
    IUnitOfWork unitOfWork,
    TimeProvider time,
    IOptions<RuntimeOptions> options,
    ILogger<DetectionService> logger)
{
    private readonly RuntimeOptions _options = options.Value;

    public async Task<Result> HandleAsync(Observation observation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observation);

        switch (observation)
        {
            case DeploymentObservation deployment:
                await RecordChangeAsync(deployment, cancellationToken);
                break;
            case TopologyObservation edge:
                await catalog.ObserveEdgeAsync(edge.TenantId, edge.EnvironmentId, edge.ServiceId, edge.TargetServiceId, cancellationToken);
                break;
            case MetricObservation metric when Rule(metric) is { } rule:
                await ObserveSignalAsync(metric, rule, cancellationToken);
                break;
        }

        return Result.Success();
    }

    /// <summary>A deterministic incident ID for the signal that opened it, so a retried open is the same incident.</summary>
    public static IncidentId IncidentIdFor(MetricObservation trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{trigger.TenantId}/{trigger.EnvironmentId}/{trigger.ServiceId}/{trigger.ObservationId}"));
        byte[] bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return IncidentId.From(new Guid(bytes));
    }

    private DetectionRule? Rule(MetricObservation metric) =>
        _options.DetectionRules().FirstOrDefault(rule => rule.ServiceId == metric.ServiceId && rule.MetricName == metric.MetricName);

    private async Task RecordChangeAsync(DeploymentObservation deployment, CancellationToken cancellationToken)
    {
        LinkedChange change = await correlation.SaveChangeAsync(deployment.TenantId, deployment.EnvironmentId, deployment.ToLinkedChange(), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);

        // A change near an open incident's services updates it: a deployment that completed after
        // detection, or one that arrived late but correlates.
        ServiceTopology topology = await topologies.GetAsync(deployment.TenantId, deployment.EnvironmentId, cancellationToken);
        CorrelationSettings settings = _options.CorrelationSettings();
        foreach (IncidentContext open in (await incidents.CorrelationCandidatesAsync(deployment.TenantId, deployment.EnvironmentId, time.GetUtcNow(), cancellationToken)).Where(candidate => candidate.IsOpen))
        {
            int? distance = open.AffectedServices.Select(service => topology.Distance(service, change.ServiceId)).Min();
            if (distance is not { } hops || hops > 1)
            {
                continue;
            }

            IReadOnlyList<LinkedChange> scoredChanges = CorrelationEngine.ScoreChanges(open.AffectedServiceId, open.SymptomOnset, [change], topology, settings);
            LinkedChange? scored = scoredChanges.Count > 0 ? scoredChanges[0] : null;
            await owner.SendAsync(open.IncidentId, new UpdateChangesCommand([scored ?? change], LinkNew: scored is not null), cancellationToken);
        }
    }

    private async Task ObserveSignalAsync(MetricObservation metric, DetectionRule rule, CancellationToken cancellationToken)
    {
        SignalStreak streak = await correlation.GetStreakAsync(metric.TenantId, metric.EnvironmentId, metric.ServiceId, metric.MetricName, cancellationToken)
            ?? new SignalStreak { TenantId = metric.TenantId, EnvironmentId = metric.EnvironmentId, ServiceId = metric.ServiceId, MetricName = metric.MetricName };
        SignalStreak updated = streak.Observe(metric.EventTime, metric.Value, rule);
        if (updated != streak)
        {
            await correlation.SaveStreakAsync(updated, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }

        if (updated.Fires(rule))
        {
            // Correlated on every firing sample, not only on a change of streak: correlation is
            // idempotent, so a redelivered sample whose first attempt failed half-way completes it.
            await CorrelateAsync(metric, updated, cancellationToken);
            return;
        }

        if (updated.HasCleared(rule) && await incidents.FindOpenAsync(metric.TenantId, metric.EnvironmentId, metric.ServiceId, cancellationToken) is { } open)
        {
            string reason = string.Create(CultureInfo.InvariantCulture, $"Symptoms cleared: {rule.MetricName} stayed below {rule.OnsetThreshold} for {rule.ClearAfter.TotalMinutes:F0} min without Sentinel remediation.");
            IncidentSnapshot? current = await owner.GetAsync(open, cancellationToken);
            if (current?.State is IncidentState.AwaitingApproval or IncidentState.Escalated
                && await owner.SendAsync(open, new ResolveClearedCommand(reason), cancellationToken) is { IsSuccess: true, Value: { Changed: true } reply })
            {
                await workflows.SignalAsync(reply.Snapshot, new IncidentSignal(IncidentSignalKind.Closed), cancellationToken);
            }
        }
    }

    private async Task CorrelateAsync(MetricObservation metric, SignalStreak streak, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        RuntimeOptions settings = _options;
        CorrelationSettings rules = settings.CorrelationSettings();
        DetectionRule rule = Rule(metric)!;
        DateTimeOffset onset = streak.OnsetAt ?? metric.EventTime;

        IReadOnlyList<IncidentContext> candidates = await incidents.CorrelationCandidatesAsync(metric.TenantId, metric.EnvironmentId, now - rules.ReopenWindow, cancellationToken);
        ServiceTopology topology = await topologies.GetAsync(metric.TenantId, metric.EnvironmentId, cancellationToken);
        IReadOnlyList<LinkedChange> changes = await correlation.GetChangesAsync(metric.TenantId, metric.EnvironmentId, [], metric.EventTime - rule.Lookback, cancellationToken);
        var signal = new CorrelationSignal(metric.ServiceId, metric.MetricName, metric.Value, onset, metric.EventTime, metric.ObservationId);

        CorrelationDecision decision = CorrelationEngine.Correlate(signal, candidates, changes, topology, settings.Suppressions(), rules);
        switch (decision)
        {
            case AbsorbIntoIncident absorb:
                await AbsorbAsync(absorb.IncidentId, metric, candidates, cancellationToken);
                break;
            case MergeIntoIncident merge:
                Result<CommandReply> linked = await owner.SendAsync(merge.IncidentId, new LinkSignalCommand(metric.ServiceId, metric.MetricName, metric.Value, [metric.ObservationId], merge.Score, merge.Reasons), cancellationToken);
                if (linked is { IsSuccess: true, Value.Changed: true })
                {
                    logger.LogInformation("Merged {Service} {Metric} into incident {IncidentId}: {Reasons}", metric.ServiceId, metric.MetricName, merge.IncidentId, string.Join("; ", merge.Reasons));
                    await AbsorbAsync(merge.IncidentId, metric, candidates, cancellationToken);
                }

                break;
            case ReopenIncident reopen:
                Result<CommandReply> reopened = await owner.SendAsync(reopen.IncidentId, new ReopenCommand(string.Join(" ", reopen.Reasons), [metric.ObservationId], onset), cancellationToken);
                if (reopened.IsSuccess)
                {
                    logger.LogInformation("Reopened incident {IncidentId}: {Reasons}", reopen.IncidentId, string.Join(" ", reopen.Reasons));
                    await workflows.EnsureStartedAsync(reopened.Value.Snapshot, cancellationToken);
                }

                break;
            case SuppressSignal suppress:
                logger.LogInformation("Suppressed {Service} {Metric}: {Reasons}", metric.ServiceId, metric.MetricName, string.Join("; ", suppress.Reasons));
                break;
            case OpenIncident open:
                await OpenAsync(metric, onset, open, cancellationToken);
                break;
        }
    }

    private async Task OpenAsync(MetricObservation metric, DateTimeOffset onset, OpenIncident decision, CancellationToken cancellationToken)
    {
        IncidentId incidentId = IncidentIdFor(metric);
        IncidentSeverity severity = SeverityPolicy.Assess(metric.Value, 1);
        Result<CommandReply> opened = await owner.SendAsync(incidentId, new OpenIncidentCommand(
            metric.TenantId,
            metric.EnvironmentId,
            metric.ServiceId,
            string.Create(CultureInfo.InvariantCulture, $"{metric.ServiceId} {metric.MetricName} at {metric.Value:F1}"),
            severity,
            onset,
            [metric.ObservationId],
            decision.Changes,
            metric.MetricName), cancellationToken);
        if (opened.IsFailure)
        {
            throw new InvalidOperationException(opened.Error.Description);
        }

        logger.LogInformation("Opened incident {IncidentId} for {Service}: {Metric} {Value} with {Changes} linked change(s): {Reasons}", incidentId, metric.ServiceId, metric.MetricName, metric.Value, decision.Changes.Count, string.Join(" ", decision.Reasons));
        await workflows.EnsureStartedAsync(opened.Value.Snapshot, cancellationToken);
    }

    /// <summary>A further sample of an open incident: raise severity if it got worse, and make sure its workflow runs.</summary>
    private async Task AbsorbAsync(IncidentId incidentId, MetricObservation metric, IReadOnlyList<IncidentContext> candidates, CancellationToken cancellationToken)
    {
        int affected = (candidates.FirstOrDefault(candidate => candidate.IncidentId == incidentId)?.AffectedServices.Count ?? 1) + 1;
        IncidentSnapshot? snapshot = await owner.GetAsync(incidentId, cancellationToken);
        if (snapshot is null)
        {
            return;
        }

        IncidentSeverity assessed = SeverityPolicy.Assess(metric.Value, affected);
        if (snapshot.IsOpen && assessed < snapshot.Severity)
        {
            string reason = string.Create(CultureInfo.InvariantCulture, $"{metric.ServiceId} {metric.MetricName} reached {metric.Value:F1} across {affected} affected service(s).");
            await owner.SendAsync(incidentId, new ChangeSeverityCommand(assessed, reason), cancellationToken);
        }

        if (snapshot.IsOpen)
        {
            await workflows.EnsureStartedAsync(snapshot, cancellationToken);
        }
    }
}

/// <summary>
/// The partition's dependency topology: the service catalog's edges and edges observed in traces,
/// rebuilt at most every <see cref="RefreshInterval"/>. A cache of facts held elsewhere.
/// </summary>
public sealed class ServiceTopologyCache(IServiceScopeFactory scopes, TimeProvider time, IOptions<RuntimeOptions> options) : IDisposable
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, (DateTimeOffset BuiltAt, ServiceTopology Topology)> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ServiceTopology> GetAsync(TenantId tenantId, EnvironmentId environmentId, CancellationToken cancellationToken)
    {
        string key = Key(tenantId, environmentId);
        DateTimeOffset now = time.GetUtcNow();
        if (_cache.TryGetValue(key, out (DateTimeOffset BuiltAt, ServiceTopology Topology) cached) && now - cached.BuiltAt < RefreshInterval)
        {
            return cached.Topology;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            RuntimeOptions settings = options.Value;
            var edges = settings.Dependencies
                .SelectMany(entry => entry.Value.Select(callee => (Caller: ServiceId.From(entry.Key), Callee: ServiceId.From(callee))))
                .ToList();
            await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
            {
                edges.AddRange(await scope.ServiceProvider.GetRequiredService<ICorrelationStore>().GetEdgesAsync(tenantId, environmentId, now - settings.Correlation.EdgeRetention, cancellationToken));
            }

            var topology = new ServiceTopology(edges);
            _cache[key] = (now, topology);
            return topology;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forces the next read to rebuild, after a new edge is observed.</summary>
    public void Invalidate(TenantId tenantId, EnvironmentId environmentId) => _cache.TryRemove(Key(tenantId, environmentId), out _);

    public void Dispose() => _gate.Dispose();

    private static string Key(TenantId tenantId, EnvironmentId environmentId) => $"{tenantId}/{environmentId}";
}
