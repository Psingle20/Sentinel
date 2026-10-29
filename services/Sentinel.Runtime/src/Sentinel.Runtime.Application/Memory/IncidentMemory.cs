using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;

namespace Sentinel.Runtime.Application.Memory;

/// <summary>Configured as <c>Runtime:Memory</c> (Phase 07).</summary>
public sealed class MemoryOptions
{
    /// <summary>How long a remembered incident is kept before a purge may remove it; a legal hold keeps it longer.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(400);

    public string PrivacyClass { get; set; } = PrivacyClasses.Internal;

    /// <summary>The redaction policy applied to the observations memory statements were derived from.</summary>
    public string RedactionPolicyVersion { get; set; } = "redaction-v1";

    /// <summary>The most similar incidents offered to one investigation.</summary>
    public int SimilarLimit { get; set; } = 3;

    public double MinimumSimilarity { get; set; } = 0.3;

    /// <summary>Whether retrieval may return incidents of another environment of the same tenant. It never crosses tenants.</summary>
    public bool CrossEnvironment { get; set; }

    /// <summary>Privacy classes whose memory is never offered as a hint to an investigation.</summary>
    public IList<string> ExcludedFromRetrieval { get; set; } = [PrivacyClasses.Restricted];
}

public static class PrivacyClasses
{
    public const string Internal = "internal";
    public const string Restricted = "restricted";
}

/// <summary>
/// The features incidents are compared on (Phase 07). Observable features are known when an
/// investigation starts: affected services, symptoms and changes. Outcome features are known only
/// once the incident closed: the cause, the culprit, the action and the outcome.
/// </summary>
public static class MemoryFeatures
{
    private static readonly string[] ObservablePrefixes = ["service:", "symptom:", "change-service:", "change-kind:"];

    private static readonly Dictionary<string, double> Weights = new(StringComparer.Ordinal)
    {
        ["service:"] = 1,
        ["symptom:"] = 2,
        ["change-service:"] = 1.5,
        ["change-kind:"] = 0.5,
        ["cause:"] = 3,
        ["culprit:"] = 2,
        ["action:"] = 1,
        ["outcome:"] = 0.5
    };

    public static IReadOnlyList<string> Observable(Incident incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        var features = new SortedSet<string>(StringComparer.Ordinal);
        foreach (ServiceId service in incident.AffectedServices)
        {
            features.Add($"service:{service}");
        }

        if (incident.TriggerMetric is { } metric)
        {
            features.Add($"symptom:{incident.AffectedServiceId}/{metric}");
        }

        foreach (SignalLinked signal in incident.LinkedSignals)
        {
            features.Add($"symptom:{signal.ServiceId}/{signal.MetricName}");
        }

        foreach (LinkedChange change in incident.Changes)
        {
            features.Add($"change-service:{change.ServiceId}");
            features.Add($"change-kind:{change.ChangeKind}");
        }

        return [.. features];
    }

    public static IReadOnlyList<string> All(Incident incident, IncidentSnapshot snapshot, MemoryAction? action, IncidentStatus status)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var features = new SortedSet<string>(Observable(incident), StringComparer.Ordinal);
        if (snapshot.Decision == DecisionResult.Diagnosed && snapshot.LeadingKey is { } key)
        {
            features.Add($"cause:{key}");
        }

        if (snapshot.Decision == DecisionResult.Diagnosed && snapshot.LeadingServiceId is { } culprit)
        {
            features.Add($"culprit:{culprit}");
        }

        if (action is not null)
        {
            features.Add($"action:{action.Kind}:{action.TargetServiceId}");
        }

        features.Add($"outcome:{status.ToString().ToLowerInvariant()}");
        return [.. features];
    }

    public static bool IsObservable(string feature) => ObservablePrefixes.Any(prefix => feature.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>Weighted Jaccard similarity of two feature sets, optionally over observable features only.</summary>
    public static (double Score, IReadOnlyList<string> Shared) Score(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right, bool observableOnly)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        HashSet<string> a = [.. left.Where(feature => !observableOnly || IsObservable(feature))];
        HashSet<string> b = [.. right.Where(feature => !observableOnly || IsObservable(feature))];
        string[] shared = [.. a.Intersect(b).Order(StringComparer.Ordinal)];
        double union = a.Union(b).Sum(Weight);
        return union <= 0 ? (0, []) : (Math.Round(shared.Sum(Weight) / union, 4), shared);
    }

    private static double Weight(string feature)
    {
        foreach ((string prefix, double weight) in Weights)
        {
            if (feature.StartsWith(prefix, StringComparison.Ordinal))
            {
                return weight;
            }
        }

        return 1;
    }
}

/// <summary>Builds an incident's memory record from its authoritative stream and the projection derived from it.</summary>
public static class IncidentMemoryBuilder
{
    public const int SchemaVersion = 1;

    public static IncidentMemoryView Build(Incident incident, IncidentDetail detail, RetentionMetadata retention, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(retention);

        var snapshot = IncidentSnapshot.Of(incident);
        MemoryAction? action = detail.Plan is { } plan ? Action(plan, incident, detail) : null;
        IncidentStatus status = detail.Summary.Status;
        Evidence[] evidence = [.. incident.AcceptedEvidence()];

        return new IncidentMemoryView
        {
            IncidentId = incident.Id,
            TenantId = incident.TenantId,
            EnvironmentId = incident.EnvironmentId,
            Title = incident.Title,
            Status = status,
            OpenedAt = incident.DetectedAt,
            ClosedAt = incident.ResolvedAt ?? (incident.State == IncidentState.Escalated ? incident.UpdatedAt : null),
            Cycle = incident.Cycle,
            StreamVersion = incident.Version,
            AffectedServices = incident.AffectedServices,
            Symptoms = Symptoms(incident),
            Changes = detail.Changes,
            HypothesisKeys = [.. incident.AllHypotheses().Select(hypothesis => hypothesis.Key).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            EvidenceIds = [.. evidence.Select(item => item.EvidenceId)],
            ObservationIds = [.. incident.TriggerObservationIds
                .Concat(evidence.SelectMany(item => item.SourceObservationIds))
                .Concat(incident.Changes.SelectMany(change => change.ObservationIds))
                .Distinct()],
            Decision = detail.Decision?.Outcome,
            Cause = snapshot.Decision == DecisionResult.Diagnosed && snapshot.LeadingStatement is { } statement
                ? new MemoryCause(snapshot.LeadingKey, statement, snapshot.LeadingServiceId, snapshot.DecisionConfidence ?? 0, snapshot.LeadingChange?.DeploymentId)
                : null,
            Action = action,
            Versions = Versions(incident, retention.RedactionPolicyVersion, dataset: null),
            Retention = retention,
            RecordedAt = now,
            Features = MemoryFeatures.All(incident, snapshot, action, status)
        };
    }

    /// <summary>The run's version manifest, derived from what the stream recorded.</summary>
    public static VersionManifest Versions(Incident incident, string? redactionPolicyVersion, string? dataset)
    {
        ArgumentNullException.ThrowIfNull(incident);

        return new VersionManifest
        {
            SchemaVersion = SchemaVersion,
            DecisionAlgorithm = incident.Decision?.AlgorithmVersion,
            Models = [.. incident.Reports
                .Select(reported => new ModelMetadata(reported.Report.ModelProvider, reported.Report.Model, reported.Report.ModelVersion, reported.Report.PromptVersion))
                .Distinct()],
            ToolCatalogVersions = [.. incident.Reports.Select(reported => reported.Report.ToolCatalogVersion).OfType<string>().Distinct(StringComparer.Ordinal)],
            PolicyVersion = incident.Plan?.PolicyVersion,
            RedactionPolicyVersion = redactionPolicyVersion,
            Dataset = dataset
        };
    }

    /// <summary>What remains of a record after a privacy deletion or a retention purge: identity and the deletion itself.</summary>
    public static IncidentMemoryView Tombstone(IncidentMemoryView memory, string actor, string reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(memory);

        return new IncidentMemoryView
        {
            IncidentId = memory.IncidentId,
            TenantId = memory.TenantId,
            EnvironmentId = memory.EnvironmentId,
            Title = "[deleted]",
            Status = memory.Status,
            OpenedAt = memory.OpenedAt,
            ClosedAt = memory.ClosedAt,
            Cycle = memory.Cycle,
            StreamVersion = memory.StreamVersion,
            Versions = new VersionManifest { SchemaVersion = SchemaVersion },
            Retention = memory.Retention with { DeletedAt = now, DeletedBy = actor, DeletionReason = reason },
            RecordedAt = now
        };
    }

    private static IReadOnlyList<SymptomSignature> Symptoms(Incident incident)
    {
        var symptoms = new List<SymptomSignature>();
        if (incident.TriggerMetric is { } metric)
        {
            symptoms.Add(new SymptomSignature(incident.AffectedServiceId, metric));
        }

        symptoms.AddRange(incident.LinkedSignals.Select(signal => new SymptomSignature(signal.ServiceId, signal.MetricName)));
        return [.. symptoms.Distinct()];
    }

    private static MemoryAction Action(PlanView plan, Incident incident, IncidentDetail detail)
    {
        RemediationAction action = plan.Plan.Actions[0];
        string kind = action switch
        {
            RollbackAction => "rollback",
            PauseBatchAction => "pause-batch",
            _ => action.GetType().Name
        };
        return new MemoryAction(
            kind,
            action.TargetServiceId,
            incident.Plan?.Summary ?? kind,
            plan.PolicyEvaluation.Decision.ToString(),
            detail.Execution?.Outcome?.ToString(),
            detail.Recovery?.Outcome.ToString());
    }
}

/// <summary>Writes an incident's memory record whenever it is closed, in the same unit of work as the events.</summary>
public sealed class IncidentMemoryRecorder(IIncidentMemoryStore memory, IOptions<RuntimeOptions> options)
{
    public async Task RecordAsync(Incident incident, IncidentDetail detail, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);

        if (incident.State is not (IncidentState.Resolved or IncidentState.Escalated))
        {
            return;
        }

        IncidentMemoryView? existing = await memory.GetAsync(incident.Id, cancellationToken);
        if (existing?.Retention.DeletedAt is not null)
        {
            // A privacy deletion is final: later events of the stream never bring the memory back.
            return;
        }

        MemoryOptions settings = options.Value.Memory;
        RetentionMetadata retention = existing?.Retention ?? new RetentionMetadata(now + settings.Retention, LegalHold: false, settings.PrivacyClass, settings.RedactionPolicyVersion);
        await memory.SaveAsync(IncidentMemoryBuilder.Build(incident, detail, retention, now), cancellationToken);
    }
}

/// <summary>A retention purge's report: what was (or, on a dry run, would be) deleted.</summary>
public sealed record PurgeReport(bool DryRun, IReadOnlyList<IncidentId> IncidentIds, DateTimeOffset EvaluatedAt);

/// <summary>
/// Incident memory (Phase 07): similar-incident retrieval within tenant, environment, privacy and
/// retention boundaries, legal holds, privacy deletion and the human-triggered retention purge.
/// </summary>
public sealed class IncidentMemoryService(IIncidentMemoryStore memory, IIncidentOwner owner, TimeProvider time, IOptions<RuntimeOptions> options, ILogger<IncidentMemoryService> logger)
{
    public Task<IncidentMemoryView?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken) => memory.GetAsync(incidentId, cancellationToken);

    /// <summary>Hints for an investigation of an open incident: remembered incidents whose observable features resemble its own.</summary>
    public async Task<IReadOnlyList<PriorIncident>> HintsForAsync(IncidentSnapshot incident, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incident);

        IReadOnlyList<SimilarIncident> similar = await SimilarAsync(incident.TenantId, incident.EnvironmentId, incident.IncidentId, incident.Features, observableOnly: true, cancellationToken);
        return [.. similar.Select(match => new PriorIncident(match.IncidentId, match.Similarity, match.SharedFeatures, match.CauseKey, match.CauseStatement, match.Outcome, match.RecordedAt))];
    }

    /// <summary>Incidents similar to a remembered or open one, compared on every feature it has.</summary>
    public async Task<IReadOnlyList<SimilarIncident>?> SimilarToAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        if (await memory.GetAsync(incidentId, cancellationToken) is { Retention.DeletedAt: null } remembered)
        {
            return await SimilarAsync(remembered.TenantId, remembered.EnvironmentId, incidentId, remembered.Features, observableOnly: false, cancellationToken);
        }

        return await owner.GetAsync(incidentId, cancellationToken) is { } open
            ? await SimilarAsync(open.TenantId, open.EnvironmentId, incidentId, open.Features, observableOnly: true, cancellationToken)
            : null;
    }

    public async Task<IncidentMemoryView?> SetLegalHoldAsync(IncidentId incidentId, bool hold, string actor, CancellationToken cancellationToken)
    {
        if (await memory.GetAsync(incidentId, cancellationToken) is not { Retention.DeletedAt: null } remembered)
        {
            return null;
        }

        IncidentMemoryView updated = remembered with { Retention = remembered.Retention with { LegalHold = hold } };
        await memory.SaveAsync(updated, cancellationToken);
        logger.LogInformation("{Actor} set the legal hold of incident {IncidentId} memory to {Hold}", actor, incidentId, hold);
        return updated;
    }

    /// <summary>Deletes one incident's memory for privacy: the record becomes a tombstone and leaves every derived index. A legal hold refuses it.</summary>
    public async Task<CleanArchitecture.BuildingBlocks.Result> DeleteAsync(IncidentId incidentId, string actor, string reason, CancellationToken cancellationToken)
    {
        if (await memory.GetAsync(incidentId, cancellationToken) is not { } remembered)
        {
            return CleanArchitecture.BuildingBlocks.Result.Failure(MemoryErrors.NotRemembered);
        }

        if (remembered.Retention.LegalHold)
        {
            return CleanArchitecture.BuildingBlocks.Result.Failure(MemoryErrors.LegalHold);
        }

        if (remembered.Retention.DeletedAt is null)
        {
            await memory.DeleteAsync(IncidentMemoryBuilder.Tombstone(remembered, actor, reason, time.GetUtcNow()), cancellationToken);
            logger.LogInformation("{Actor} deleted incident {IncidentId} memory: {Reason}", actor, incidentId, reason);
        }

        return CleanArchitecture.BuildingBlocks.Result.Success();
    }

    /// <summary>
    /// The retention purge. It is never scheduled: an operator runs it, a dry run first, and only
    /// records past their retention and not on legal hold are deleted.
    /// </summary>
    public async Task<PurgeReport> PurgeAsync(bool dryRun, string actor, int limit, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        IReadOnlyList<IncidentMemoryView> expired = await memory.ExpiredAsync(now, Math.Clamp(limit, 1, 1000), cancellationToken);
        if (!dryRun)
        {
            foreach (IncidentMemoryView record in expired)
            {
                await memory.DeleteAsync(IncidentMemoryBuilder.Tombstone(record, actor, string.Create(CultureInfo.InvariantCulture, $"Retention expired at {record.Retention.RetainUntil:O}."), now), cancellationToken);
            }

            logger.LogInformation("{Actor} purged {Count} expired incident memory record(s)", actor, expired.Count);
        }

        return new PurgeReport(dryRun, [.. expired.Select(record => record.IncidentId)], now);
    }

    private async Task<IReadOnlyList<SimilarIncident>> SimilarAsync(TenantId tenantId, EnvironmentId environmentId, IncidentId self, IReadOnlyList<string> features, bool observableOnly, CancellationToken cancellationToken)
    {
        MemoryOptions settings = options.Value.Memory;
        string[] probe = [.. features.Where(feature => !observableOnly || MemoryFeatures.IsObservable(feature))];
        if (probe.Length == 0)
        {
            return [];
        }

        IReadOnlyList<IncidentMemoryView> candidates = await memory.CandidatesAsync(tenantId, settings.CrossEnvironment ? null : environmentId, probe, time.GetUtcNow(), 50, cancellationToken);
        return [.. candidates
            .Where(candidate => candidate.IncidentId != self && candidate.TenantId == tenantId && !settings.ExcludedFromRetrieval.Contains(candidate.Retention.PrivacyClass))
            .Select(candidate => (Candidate: candidate, Match: MemoryFeatures.Score(probe, candidate.Features, observableOnly)))
            .Where(scored => scored.Match.Score >= settings.MinimumSimilarity)
            .OrderByDescending(scored => scored.Match.Score)
            .ThenByDescending(scored => scored.Candidate.RecordedAt)
            .Take(settings.SimilarLimit)
            .Select(scored => new SimilarIncident(
                scored.Candidate.IncidentId,
                scored.Match.Score,
                scored.Match.Shared,
                scored.Candidate.Cause?.Key,
                scored.Candidate.Cause?.Statement,
                scored.Candidate.Status.ToString().ToLowerInvariant(),
                scored.Candidate.RecordedAt))];
    }
}

public static class MemoryErrors
{
    public static readonly CleanArchitecture.BuildingBlocks.Error NotRemembered = CleanArchitecture.BuildingBlocks.Error.NotFound("Memory.NotRemembered", "The incident has no memory record.");

    public static readonly CleanArchitecture.BuildingBlocks.Error LegalHold = CleanArchitecture.BuildingBlocks.Error.Conflict("Memory.LegalHold", "The incident's memory is on legal hold and cannot be deleted.");
}
