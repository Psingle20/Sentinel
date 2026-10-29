using System.Globalization;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents;
using RecoveryEvaluated = Sentinel.Runtime.Domain.Incidents.Events.RecoveryEvaluated;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Domain.Incidents.Events;

namespace Sentinel.Runtime.Application.Memory;

/// <summary>
/// Generates a postmortem from the authoritative history (Phase 07): the incident's events and the
/// projection derived from them. Every fact names its sources, so a reader can trace it to the
/// event, evidence or observation it states. Nothing is summarized by a model; the narrative is
/// written by people and stored by Api.
/// </summary>
public static class PostmortemBuilder
{
    public static Postmortem Build(IReadOnlyList<StoredEvent> history, IncidentDetail detail, VersionManifest versions, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(versions);

        var reportedAt = new Dictionary<InvestigationId, long>();
        long? decidedAt = null;
        long? proposedAt = null;
        long? recoveredAt = null;
        long detectedAt = history[0].Version;
        foreach (StoredEvent stored in history)
        {
            switch (stored.Event)
            {
                case InvestigationReported reported:
                    reportedAt[reported.Report.InvestigationId] = stored.Version;
                    break;
                case DecisionMade:
                    decidedAt = stored.Version;
                    break;
                case PlanProposed:
                    proposedAt = stored.Version;
                    break;
                case RecoveryEvaluated:
                    recoveredAt = stored.Version;
                    break;
            }
        }

        return new Postmortem
        {
            IncidentId = detail.Summary.IncidentId,
            Title = detail.Summary.Title,
            Status = detail.Summary.Status,
            StreamVersion = history[^1].Version,
            GeneratedAt = now,
            Summary = Summary(detail, detectedAt, decidedAt, proposedAt, recoveredAt),
            Timeline = [.. detail.Timeline.Select(entry => new PostmortemFact(entry.Summary, [Event(entry.StreamVersion)], entry.OccurredAt))],
            Evidence = [.. detail.Evidence
                .Where(evidence => evidence.Status == EvidenceStatus.Accepted)
                .Select(evidence => new PostmortemFact(
                    $"{evidence.Finding.Direction}: {evidence.Finding.Statement}",
                    [
                        new SourceRef("evidence", evidence.Finding.EvidenceId.ToString()),
                        new SourceRef("investigation", evidence.InvestigationId.ToString()),
                        .. reportedAt.TryGetValue(evidence.InvestigationId, out long version) ? [Event(version)] : Array.Empty<SourceRef>(),
                        .. evidence.Finding.SourceObservationIds.Select(observation => new SourceRef("observation", observation.ToString()))
                    ],
                    evidence.Finding.EventTimeRange.From))],
            Decision = Decision(detail, decidedAt),
            Actions = Actions(detail, history),
            Recovery = Recovery(detail, recoveredAt),
            OpenQuestions = OpenQuestions(detail, decidedAt),
            Versions = versions
        };
    }

    private static PostmortemFact Summary(IncidentDetail detail, long detectedAt, long? decidedAt, long? proposedAt, long? recoveredAt)
    {
        HypothesisView? leader = Leader(detail);
        string cause = detail.Decision switch
        {
            null => "No decision was made.",
            { Outcome: DecisionOutcome.Diagnosed } when leader is not null => $"Diagnosed: {leader.Finding.Statement}",
            _ => "Sentinel abstained: the evidence did not establish a cause."
        };
        string action = detail.Plan is { } plan ? $" Plan: {string.Join(", ", plan.Plan.Actions.Select(Describe))} ({plan.Status})." : string.Empty;
        string recovery = detail.Recovery is { } outcome ? $" Recovery: {outcome.Outcome}." : string.Empty;
        SourceRef[] sources = [Event(detectedAt), .. new[] { decidedAt, proposedAt, recoveredAt }.OfType<long>().Select(Event)];
        return new PostmortemFact($"{detail.Summary.Title}, {detail.Summary.Status}. {cause}{action}{recovery}", sources, detail.Summary.OpenedAt);
    }

    private static List<PostmortemFact> Decision(IncidentDetail detail, long? decidedAt)
    {
        if (detail.Decision is not { } decision || decidedAt is not { } version)
        {
            return [];
        }

        SourceRef made = Event(version);
        var facts = new List<PostmortemFact>
        {
            new(string.Create(CultureInfo.InvariantCulture, $"{decision.Outcome} by {decision.AlgorithmVersion ?? "falsification-v1"} at confidence {decision.Confidence:F2} with evidence completeness {decision.EvidenceCompleteness:F2}: {decision.Rationale}"), [made, new SourceRef("decision", decision.DecisionId.ToString())], decision.DecidedAt)
        };
        facts.AddRange(decision.Checks.Select(check => new PostmortemFact($"Check {check.Name} {(check.Passed ? "passed" : "failed")}: {check.Detail}", [made], decision.DecidedAt)));
        facts.AddRange(decision.Assessments.Select(assessment => new PostmortemFact(
            string.Create(CultureInfo.InvariantCulture, $"{Statement(detail, assessment.HypothesisId)}: score {assessment.Score:F2}, {assessment.IndependentSupport} independent supporting and {assessment.IndependentContradiction} contradicting families{(assessment.Eliminated ? ", eliminated" : string.Empty)}. {assessment.Reason}"),
            [made, new SourceRef("hypothesis", assessment.HypothesisId.ToString())],
            decision.DecidedAt)));
        return facts;
    }

    private static List<PostmortemFact> Actions(IncidentDetail detail, IReadOnlyList<StoredEvent> history)
    {
        var facts = new List<PostmortemFact>();
        foreach (StoredEvent stored in history)
        {
            PostmortemFact? fact = stored.Event switch
            {
                PlanProposed proposed => new PostmortemFact(
                    $"Proposed plan v{proposed.Plan.Version} to {proposed.Plan.Summary ?? "remediate"}; policy {proposed.Plan.PolicyVersion} said {proposed.Plan.Policy} ({string.Join("; ", proposed.Plan.PolicyTrace)}).",
                    [Event(stored.Version), new SourceRef("plan", proposed.Plan.PlanId.ToString())],
                    proposed.OccurredAt),
                PlanApproved approved => new PostmortemFact($"{approved.ApproverId} approved plan v{approved.PlanVersion} with hash {approved.PlanHash[..Math.Min(20, approved.PlanHash.Length)]}.", [Event(stored.Version)], approved.OccurredAt),
                PlanRejected rejected => new PostmortemFact($"{rejected.RejectedBy} rejected plan v{rejected.PlanVersion}: {rejected.Reason}", [Event(stored.Version)], rejected.OccurredAt),
                ApprovalRevoked revoked => new PostmortemFact($"{revoked.RevokedBy} revoked the approval of plan v{revoked.PlanVersion}: {revoked.Reason}", [Event(stored.Version)], revoked.OccurredAt),
                ExecutionRequested requested => new PostmortemFact(string.Create(CultureInfo.InvariantCulture, $"Execution of plan v{requested.PlanVersion} requested with fencing token {requested.FencingToken}."), [Event(stored.Version), new SourceRef("execution", requested.IdempotencyKey)], requested.OccurredAt),
                ExecutionStarted started => new PostmortemFact($"Runtime confirmed the start of plan v{started.PlanVersion}.", [Event(stored.Version)], started.OccurredAt),
                ExecutionReported reported => new PostmortemFact($"Executor reported {reported.Result}{(reported.Detail is null ? "." : $": {reported.Detail}")}", [Event(stored.Version)], reported.OccurredAt),
                IncidentEscalated escalated => new PostmortemFact($"Escalated to responders: {escalated.Reason}", [Event(stored.Version)], escalated.OccurredAt),
                _ => null
            };
            if (fact is not null)
            {
                facts.Add(fact);
            }
        }

        return detail.Plan is null && facts.Count == 0 ? [] : facts;
    }

    private static List<PostmortemFact> Recovery(IncidentDetail detail, long? recoveredAt)
    {
        if (detail.Recovery is not { } recovery || recoveredAt is not { } version)
        {
            return [];
        }

        SourceRef evaluated = Event(version);
        var facts = new List<PostmortemFact>
        {
            new($"Recovery {recovery.Outcome}, observed through {recovery.ObservedThrough:O}{(recovery.Reason is null ? "." : $": {recovery.Reason}")}", [evaluated], recovery.EvaluatedAt)
        };
        facts.AddRange(recovery.Measurements.Select(measurement => new PostmortemFact(
            string.Create(CultureInfo.InvariantCulture, $"{measurement.Criterion.ServiceId} {measurement.Criterion.MetricName} {measurement.Criterion.Comparison} {measurement.Criterion.Threshold}: observed {Observed(measurement)}, {Met(measurement)}."),
            [evaluated],
            recovery.EvaluatedAt)));
        return facts;
    }

    private static string Observed(RecoveryMeasurement measurement) =>
        measurement.ObservedValue is { } value ? value.ToString("G4", CultureInfo.InvariantCulture) : "nothing";

    private static string Met(RecoveryMeasurement measurement) => measurement.Met switch
    {
        true => "met",
        false => "not met",
        null => "unknown"
    };

    private static List<PostmortemFact> OpenQuestions(IncidentDetail detail, long? decidedAt)
    {
        var facts = new List<PostmortemFact>();
        if (detail.Decision is { } decision && decidedAt is { } version)
        {
            facts.AddRange(decision.MissingEvidence.Select(missing => new PostmortemFact($"Not determined: {missing}", [Event(version)], decision.DecidedAt)));
            facts.AddRange(decision.DissentingHypothesisIds.Select(dissent => new PostmortemFact($"Dissent kept: {Statement(detail, dissent)}", [Event(version), new SourceRef("hypothesis", dissent.ToString())], decision.DecidedAt)));
        }

        facts.AddRange(detail.Investigations.Where(investigation => investigation.TimedOut).Select(investigation => new PostmortemFact(
            $"The {investigation.Role ?? "generalist"} investigation timed out; its findings are missing.",
            [new SourceRef("investigation", investigation.InvestigationId.ToString())],
            investigation.RequestedAt)));
        return facts;
    }

    private static HypothesisView? Leader(IncidentDetail detail) =>
        detail.Decision?.LeadingHypothesisId is { } leading ? detail.Hypotheses.FirstOrDefault(hypothesis => hypothesis.Finding.HypothesisId == leading) : null;

    private static string Statement(IncidentDetail detail, HypothesisId hypothesisId) =>
        detail.Hypotheses.FirstOrDefault(hypothesis => hypothesis.Finding.HypothesisId == hypothesisId || (hypothesis.Lineage ?? []).Contains(hypothesisId))?.Finding.Statement ?? hypothesisId.ToString();

    private static string Describe(Sentinel.Contracts.Remediation.RemediationAction action) => action switch
    {
        Sentinel.Contracts.Remediation.RollbackAction rollback => $"roll {rollback.TargetServiceId} back from {rollback.FromRevision} to {rollback.ToRevision}",
        Sentinel.Contracts.Remediation.PauseBatchAction pause => $"pause the {pause.TargetServiceId} batch",
        _ => action.GetType().Name
    };

    private static SourceRef Event(long version) => new("event", version.ToString(CultureInfo.InvariantCulture));
}
