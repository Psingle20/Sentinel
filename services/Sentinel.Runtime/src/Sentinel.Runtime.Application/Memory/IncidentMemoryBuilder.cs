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
            QuarantineMessageAction => "quarantine-message",
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
