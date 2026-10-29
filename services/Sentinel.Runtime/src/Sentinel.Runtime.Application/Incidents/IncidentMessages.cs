using System.Collections.Concurrent;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Domain.Incidents;
using ContractRecoveryEvaluated = Sentinel.Contracts.Incidents.RecoveryEvaluated;
using DomainRecoveryEvaluated = Sentinel.Runtime.Domain.Incidents.Events.RecoveryEvaluated;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Domain.Incidents.Events;
using Sentinel.Runtime.Application.Projections;

namespace Sentinel.Runtime.Application.Incidents;

/// <summary>Maps new stream events to the integration messages other services depend on (ADR-012).</summary>
public static class IncidentMessages
{
    public static void Enqueue(
        IOutbox outbox,
        Incident incident,
        IReadOnlyList<StoredEvent> appended,
        IncidentState? stateBefore,
        InvestigationBudget budget,
        IReadOnlyList<ServiceId> dependencies)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(appended);

        var metadata = new MessageMetadata(
            CorrelationId: incident.Id.ToString(),
            Headers: SentinelHeaders.For(incident.TenantId, incident.EnvironmentId, incident.AffectedServiceId, incident.Id));

        foreach (StoredEvent stored in appended)
        {
            switch (stored.Event)
            {
                case IncidentDetected detected:
                    outbox.Enqueue(new IncidentOpened
                    {
                        TenantId = incident.TenantId,
                        EnvironmentId = incident.EnvironmentId,
                        IncidentId = incident.Id,
                        AffectedServiceId = incident.AffectedServiceId,
                        Title = detected.Title,
                        Severity = detected.Severity.ToContract(),
                        OpenedAt = detected.OccurredAt,
                        TriggerObservationIds = detected.TriggerObservationIds,
                        LinkedDeploymentObservationIds = [.. detected.LinkedChanges.SelectMany(change => change.ObservationIds)],
                        StreamVersion = stored.Version
                    }, metadata);
                    break;
                case InvestigationRequested requested:
                    outbox.Enqueue(Investigate(incident, requested, budget, dependencies), metadata);
                    break;
                case InvestigationReported reported:
                    outbox.Enqueue(new IncidentBlackboardUpdated
                    {
                        TenantId = incident.TenantId,
                        EnvironmentId = incident.EnvironmentId,
                        IncidentId = incident.Id,
                        InvestigationId = reported.Report.InvestigationId,
                        Role = reported.Report.Role,
                        AcceptedEvidence = reported.Report.Evidence.Count - reported.RejectedEvidence.Count,
                        RejectedEvidence = reported.RejectedEvidence.Count,
                        HypothesisKeys = [.. incident.AllHypotheses().Select(hypothesis => hypothesis.Key ?? hypothesis.HypothesisId.ToString())],
                        UpdatedAt = reported.OccurredAt,
                        StreamVersion = stored.Version
                    }, metadata);
                    break;
                case DecisionMade made:
                    DecisionView view = made.Decision.ToView();
                    outbox.Enqueue(new DecisionRecorded
                    {
                        TenantId = incident.TenantId,
                        EnvironmentId = incident.EnvironmentId,
                        IncidentId = incident.Id,
                        DecisionId = view.DecisionId,
                        Outcome = view.Outcome,
                        LeadingHypothesisId = view.LeadingHypothesisId,
                        RejectedHypothesisIds = view.RejectedHypothesisIds,
                        Confidence = view.Confidence,
                        EvidenceCompleteness = view.EvidenceCompleteness,
                        Calibration = view.Calibration,
                        Rationale = view.Rationale,
                        DecidedAt = view.DecidedAt,
                        StreamVersion = stored.Version
                    }, metadata);
                    break;
                case PlanProposed proposed:
                    outbox.Enqueue(new RemediationPlanProposed
                    {
                        TenantId = incident.TenantId,
                        EnvironmentId = incident.EnvironmentId,
                        IncidentId = incident.Id,
                        Plan = proposed.Plan.PlanDocument(),
                        PolicyEvaluation = proposed.Plan.PolicyEvaluation(),
                        StreamVersion = stored.Version
                    }, metadata);
                    break;
                case ExecutionRequested requested:
                    outbox.Enqueue(new ExecuteRemediation
                    {
                        TenantId = incident.TenantId,
                        EnvironmentId = incident.EnvironmentId,
                        IncidentId = incident.Id,
                        Plan = incident.Plan!.PlanDocument(),
                        Attestation = incident.Approval!.Attestation,
                        FencingToken = requested.FencingToken,
                        IdempotencyKey = requested.IdempotencyKey,
                        RequestedAt = requested.OccurredAt
                    }, metadata);
                    break;
                case DomainRecoveryEvaluated evaluated:
                    outbox.Enqueue(new ContractRecoveryEvaluated
                    {
                        TenantId = incident.TenantId,
                        EnvironmentId = incident.EnvironmentId,
                        IncidentId = incident.Id,
                        PlanId = evaluated.PlanId,
                        Outcome = evaluated.Result.ToContract(),
                        Measurements = [.. evaluated.Measurements.Select(measurement => measurement.ToContract())],
                        ObservedThrough = evaluated.ObservedThrough,
                        EvaluatedAt = evaluated.OccurredAt,
                        StreamVersion = stored.Version
                    }, metadata);
                    break;
            }
        }

        if (stateBefore is not null && stateBefore != incident.State)
        {
            outbox.Enqueue(new IncidentStatusChanged
            {
                TenantId = incident.TenantId,
                EnvironmentId = incident.EnvironmentId,
                IncidentId = incident.Id,
                Status = incident.State.ToStatus(),
                Reason = incident.ClosingReason,
                ChangedAt = incident.UpdatedAt,
                StreamVersion = incident.Version
            }, metadata);
        }
    }

    private static InvestigateIncident Investigate(Incident incident, InvestigationRequested requested, InvestigationBudget budget, IReadOnlyList<ServiceId> dependencies)
    {
        bool verifier = requested.Role == InvestigationRoles.Verifier;
        HashSet<HypothesisId> challenged = [.. requested.ChallengedHypothesisIds ?? []];
        return new InvestigateIncident
        {
            TenantId = incident.TenantId,
            EnvironmentId = incident.EnvironmentId,
            IncidentId = incident.Id,
            InvestigationId = requested.InvestigationId,
            AffectedServiceId = incident.AffectedServiceId,
            // The catalog's dependencies, every service merged into the incident and any service with a correlated change.
            RelatedServiceIds = [.. dependencies
                .Concat(incident.AffectedServices)
                .Concat(incident.Changes.Select(change => change.ServiceId))
                .Where(service => service != incident.AffectedServiceId)
                .Distinct()],
            Objective = requested.Objective,
            ObservationWindow = new Sentinel.Contracts.TimeRange(requested.WindowFrom, requested.WindowTo),
            Budget = budget,
            SymptomOnset = incident.SymptomOnset,
            Role = requested.Role,
            Hypotheses = verifier ? [.. incident.AllHypotheses().Where(hypothesis => challenged.Contains(hypothesis.HypothesisId)).Select(hypothesis => hypothesis.ToContract())] : null,
            Evidence = verifier ? [.. incident.AcceptedEvidence().Select(evidence => evidence.ToContract())] : null,
            SimilarIncidents = requested.SimilarIncidents is { Count: > 0 } similar
                ? [.. similar.Select(prior => new SimilarIncident(prior.IncidentId, prior.Similarity, prior.SharedFeatures, prior.CauseKey, prior.CauseStatement, prior.Outcome, prior.RecordedAt))]
                : null
        };
    }
}
