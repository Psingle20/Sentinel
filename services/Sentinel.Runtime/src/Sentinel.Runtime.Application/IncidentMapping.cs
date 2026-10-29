using System.Text.Json;
using Sentinel.Contracts;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Domain.Incidents;
using ContractDirection = Sentinel.Contracts.Investigations.EvidenceDirection;
using ContractVerdict = Sentinel.Contracts.Investigations.ChallengeVerdict;
using DomainDirection = Sentinel.Runtime.Domain.Incidents.EvidenceDirection;
using DomainVerdict = Sentinel.Runtime.Domain.Incidents.ChallengeVerdict;

namespace Sentinel.Runtime.Application;

/// <summary>Translates between Runtime's domain and the shared contracts (ADR-007: the domain never sees contracts).</summary>
public static class IncidentMapping
{
    public static IncidentStatus ToStatus(this IncidentState state) => state switch
    {
        IncidentState.Detected => IncidentStatus.Detected,
        IncidentState.Investigating => IncidentStatus.Investigating,
        IncidentState.AwaitingApproval => IncidentStatus.AwaitingApproval,
        IncidentState.Remediating => IncidentStatus.Remediating,
        IncidentState.Verifying => IncidentStatus.Verifying,
        IncidentState.Resolved => IncidentStatus.Resolved,
        _ => IncidentStatus.Escalated
    };

    public static Severity ToContract(this IncidentSeverity severity) => severity switch
    {
        IncidentSeverity.Critical => Severity.Critical,
        IncidentSeverity.High => Severity.High,
        IncidentSeverity.Medium => Severity.Medium,
        _ => Severity.Low
    };

    public static LinkedChange ToLinkedChange(this DeploymentObservation deployment) => new()
    {
        DeploymentId = deployment.DeploymentId,
        ServiceId = deployment.ServiceId,
        Version = deployment.Version,
        PreviousVersion = deployment.PreviousVersion,
        ChangeKind = deployment.ChangeKind,
        StartedAt = deployment.StartedAt,
        CompletedAt = deployment.Status == "completed" ? deployment.CompletedAt : null,
        Failed = deployment.Status == "failed",
        ObservationIds = [deployment.ObservationId]
    };

    public static InvestigationReport ToReport(this InvestigationCompleted completed) => new()
    {
        InvestigationId = completed.InvestigationId,
        AgentExecutionId = completed.AgentExecutionId,
        Result = completed.Outcome switch
        {
            InvestigationOutcome.Completed => InvestigationResult.Completed,
            InvestigationOutcome.Abstained => InvestigationResult.Abstained,
            _ => InvestigationResult.Failed
        },
        Evidence = [.. completed.Evidence.Select(ToDomain)],
        Hypotheses = [.. completed.Hypotheses.Select(ToDomain)],
        AbstentionReason = completed.AbstentionReason,
        ModelProvider = completed.Model.Provider,
        Model = completed.Model.Model,
        ModelVersion = completed.Model.ModelVersion,
        PromptVersion = completed.Model.PromptVersion,
        ToolCatalogVersion = completed.ToolCatalogVersion,
        CompletedAt = completed.CompletedAt,
        Role = completed.Role,
        Challenges = [.. (completed.Challenges ?? []).Select(challenge => new Challenge(
            challenge.HypothesisId,
            challenge.Falsifier,
            challenge.Verdict switch
            {
                ContractVerdict.Survived => DomainVerdict.Survived,
                ContractVerdict.Falsified => DomainVerdict.Falsified,
                _ => DomainVerdict.Inconclusive
            },
            challenge.EvidenceIds,
            challenge.Detail))],
        Unknowns = completed.Unknowns ?? []
    };

    public static ChallengeView ToView(this Challenge challenge, InvestigationId investigationId)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        return new ChallengeView(
            challenge.HypothesisId,
            challenge.Falsifier,
            challenge.Verdict switch
            {
                DomainVerdict.Survived => ContractVerdict.Survived,
                DomainVerdict.Falsified => ContractVerdict.Falsified,
                _ => ContractVerdict.Inconclusive
            },
            challenge.Detail,
            challenge.EvidenceIds,
            investigationId);
    }

    public static LinkedChangeView ToView(this LinkedChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new LinkedChangeView(change.DeploymentId, change.ServiceId, change.ChangeKind, change.Version, change.PreviousVersion, change.StartedAt, change.CompletedAt, change.Failed, change.CorrelationScore, change.CorrelationReasons, change.ObservationIds);
    }

    public static Evidence ToDomain(this EvidenceFinding finding) => new()
    {
        EvidenceId = finding.EvidenceId,
        Statement = finding.Statement,
        Direction = finding.Direction switch
        {
            ContractDirection.Supports => DomainDirection.Supports,
            ContractDirection.Contradicts => DomainDirection.Contradicts,
            _ => DomainDirection.Neutral
        },
        Strength = finding.Strength,
        SourceObservationIds = finding.SourceObservationIds,
        HypothesisIds = finding.HypothesisIds,
        From = finding.EventTimeRange.From,
        To = finding.EventTimeRange.To,
        ToolCallId = finding.ToolCallId,
        IndependenceGroup = finding.IndependenceGroup,
        Partial = finding.Partial
    };

    public static Hypothesis ToDomain(this HypothesisFinding finding) => new()
    {
        HypothesisId = finding.HypothesisId,
        Statement = finding.Statement,
        CausalChain = finding.CausalChain,
        SuspectedServiceId = finding.SuspectedServiceId,
        SuspectedChangeObservationId = finding.SuspectedChangeObservationId,
        SupportingEvidenceIds = finding.SupportingEvidenceIds,
        ContradictingEvidenceIds = finding.ContradictingEvidenceIds,
        Key = finding.Key
    };

    public static EvidenceFinding ToContract(this Evidence evidence) => new()
    {
        EvidenceId = evidence.EvidenceId,
        Statement = evidence.Statement,
        Direction = evidence.Direction switch
        {
            DomainDirection.Supports => ContractDirection.Supports,
            DomainDirection.Contradicts => ContractDirection.Contradicts,
            _ => ContractDirection.Neutral
        },
        Strength = evidence.Strength,
        SourceObservationIds = evidence.SourceObservationIds,
        HypothesisIds = evidence.HypothesisIds,
        EventTimeRange = new TimeRange(evidence.From, evidence.To),
        ToolCallId = evidence.ToolCallId,
        IndependenceGroup = evidence.IndependenceGroup,
        Partial = evidence.Partial
    };

    public static HypothesisFinding ToContract(this Hypothesis hypothesis) => new()
    {
        HypothesisId = hypothesis.HypothesisId,
        Statement = hypothesis.Statement,
        CausalChain = hypothesis.CausalChain,
        SuspectedServiceId = hypothesis.SuspectedServiceId,
        SuspectedChangeObservationId = hypothesis.SuspectedChangeObservationId,
        SupportingEvidenceIds = hypothesis.SupportingEvidenceIds,
        ContradictingEvidenceIds = hypothesis.ContradictingEvidenceIds,
        Key = hypothesis.Key
    };

    public static DecisionView ToView(this Decision decision) => new()
    {
        DecisionId = decision.DecisionId,
        Outcome = decision.Result == DecisionResult.Diagnosed ? DecisionOutcome.Diagnosed : DecisionOutcome.Abstained,
        LeadingHypothesisId = decision.LeadingHypothesisId,
        RejectedHypothesisIds = decision.RejectedHypothesisIds,
        Confidence = decision.Confidence,
        EvidenceCompleteness = decision.EvidenceCompleteness,
        Calibration = Calibration.Uncalibrated,
        Rationale = decision.Rationale,
        Checks = [.. decision.Checks.Select(check => new DecisionCheck(check.Name, check.Passed, check.Detail))],
        DecidedAt = decision.DecidedAt,
        DissentingHypothesisIds = decision.DissentingHypothesisIds,
        Assessments = [.. decision.Assessments.Select(assessment => new HypothesisAssessmentView(assessment.HypothesisId, assessment.Score, assessment.IndependentSupport, assessment.IndependentContradiction, assessment.Falsified, assessment.Reason))],
        MissingEvidence = decision.MissingEvidence,
        AlgorithmVersion = decision.AlgorithmVersion,
        InputsHash = decision.InputsHash,
        SupersededBy = decision.SupersededBy
    };

    public static PolicyDecision ToContract(this PolicyVerdict verdict) => verdict switch
    {
        PolicyVerdict.Allow => PolicyDecision.Allow,
        PolicyVerdict.Deny => PolicyDecision.Deny,
        PolicyVerdict.SuggestionOnly => PolicyDecision.SuggestionOnly,
        _ => PolicyDecision.ApprovalRequired
    };

    public static PolicyVerdict ToDomain(this PolicyDecision decision) => decision switch
    {
        PolicyDecision.Allow => PolicyVerdict.Allow,
        PolicyDecision.Deny => PolicyVerdict.Deny,
        PolicyDecision.SuggestionOnly => PolicyVerdict.SuggestionOnly,
        _ => PolicyVerdict.ApprovalRequired
    };

    public static ExecutionResult ToDomain(this ExecutionOutcome outcome) => Enum.Parse<ExecutionResult>(outcome.ToString());

    public static ExecutionOutcome ToContract(this ExecutionResult result) => Enum.Parse<ExecutionOutcome>(result.ToString());

    public static RecoveryOutcome ToContract(this RecoveryResult result) => Enum.Parse<RecoveryOutcome>(result.ToString());

    public static DecisionOutcome ToContract(this DecisionResult result) => result == DecisionResult.Diagnosed ? DecisionOutcome.Diagnosed : DecisionOutcome.Abstained;

    public static RemediationPlan PlanDocument(this ProposedPlan plan) =>
        JsonSerializer.Deserialize<RemediationPlan>(plan.Document, SentinelJson.Options)!;

    public static PolicyEvaluation PolicyEvaluation(this ProposedPlan plan) =>
        new(plan.Policy.ToContract(), plan.PolicyVersion, plan.PolicyTrace, plan.ProposedAt);

    public static RecoveryMeasurement ToContract(this Measurement measurement) => new(
        new RecoveryCriterion
        {
            ServiceId = ServiceId.From(measurement.ServiceId),
            MetricName = measurement.MetricName,
            Comparison = Enum.Parse<ThresholdComparison>(measurement.Comparison),
            Threshold = measurement.Threshold
        },
        measurement.ObservedValue,
        measurement.Met,
        measurement.BaselineValue);
}
