using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Abstractions.Providers;
using Sentinel.Runtime.Application.Incidents.Commands;
using Sentinel.Runtime.Application.Options;
using Sentinel.Runtime.Application.Recovery;
using Sentinel.Runtime.Application.Projections;

namespace Sentinel.Runtime.Application.Incidents;

/// <summary>
/// The incident workflow's steps (ADR-005): each one reads the incident through its writer, does the
/// step's I/O, and changes the incident only by an idempotent command. A retried or duplicated step
/// either appends nothing new or is refused by the writer's invariants (ADR-004). The workflow
/// passes only incident IDs to these steps and receives only small summaries back.
/// </summary>
public sealed class IncidentSteps(
    IIncidentOwner owner,
    ICorrelationStore correlation,
    IPolicyEvaluator policy,
    RecoveryAssessor recovery,
    Memory.IncidentSimilarity similarity,
    TimeProvider time,
    IOptions<RuntimeOptions> options,
    ILogger<IncidentSteps> logger)
{
    private const string PoisonMessagePrefix = "poison-message:";

    // A message received this often is no longer one a retry will process; the cap bounds what one approval moves.
    private const int PoisonReceives = 3;
    private const int PoisonMessagesAtMost = 5;

    /// <summary>An ID derived from the incident, its cycle and the step, so a retried step names the same investigation.</summary>
    public static InvestigationId StepInvestigationId(IncidentId incidentId, int cycle, string step)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{incidentId}/{cycle}/{step}")))[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return InvestigationId.From(new Guid(bytes));
    }

    public Task<IncidentSnapshot?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken) => owner.GetAsync(incidentId, cancellationToken);

    /// <summary>
    /// Assigns one investigation per configured role, in parallel (Phase 04 specialist fan-out), with
    /// the remembered incidents that resemble this one as hints (Phase 07). The hints are recorded in
    /// the request events, so a replay sees the same investigation inputs.
    /// </summary>
    public async Task<IReadOnlyList<InvestigationId>> RequestInvestigationsAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        IncidentSnapshot incident = await RequireAsync(incidentId, cancellationToken);
        if (incident.Decision is not null || incident.State is not (IncidentState.Detected or IncidentState.Investigating))
        {
            return [];
        }

        RuntimeOptions settings = options.Value;
        DateTimeOffset now = time.GetUtcNow();
        IReadOnlyList<PriorIncident> similar = await similarity.HintsForAsync(incident, cancellationToken);
        InvestigationSpec[] specs = [.. settings.Investigation.EffectiveRoles.Select(role => new InvestigationSpec(
            StepInvestigationId(incidentId, incident.Cycle, $"investigate:{role}"),
            role == InvestigationRoles.Generalist ? null : role,
            Objective(incident, role),
            incident.SymptomOnset - settings.InvestigationWindow,
            now,
            SimilarIncidents: similar))];

        await SendAsync(incidentId, new RequestInvestigationsCommand(specs), cancellationToken);
        return [.. specs.Select(spec => spec.InvestigationId)];
    }

    /// <summary>Assigns the adversarial verifier to every hypothesis of the cycle (Phase 05). Null when there is nothing to challenge.</summary>
    public async Task<InvestigationId?> RequestChallengeAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        IncidentSnapshot incident = await RequireAsync(incidentId, cancellationToken);
        if (incident.HypothesisIds.Count == 0 || incident.Decision is not null || incident.State != IncidentState.Investigating)
        {
            return null;
        }

        RuntimeOptions settings = options.Value;
        InvestigationId challengeId = StepInvestigationId(incidentId, incident.Cycle, "challenge");
        await SendAsync(incidentId, new RequestInvestigationsCommand(
        [
            new InvestigationSpec(
                challengeId,
                InvestigationRoles.Verifier,
                $"Try to falsify each hypothesis about {incident.Title}: look for contradicting evidence and alternative explanations.",
                incident.SymptomOnset - settings.InvestigationWindow,
                time.GetUtcNow(),
                incident.HypothesisIds)
        ]), cancellationToken);
        return challengeId;
    }

    public Task TimeOutAsync(IncidentId incidentId, IReadOnlyList<InvestigationId> investigationIds, CancellationToken cancellationToken) =>
        SendAsync(incidentId, new TimeOutInvestigationsCommand(investigationIds), cancellationToken);

    /// <summary>Refreshes what is known about the linked changes, then decides from the incident's own evidence.</summary>
    public async Task<IncidentSnapshot> DecideAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        IncidentSnapshot incident = await RequireAsync(incidentId, cancellationToken);
        if (incident.Decision is not null)
        {
            return incident;
        }

        // A deployment that finished after detection changes the precedence check.
        var latest = new List<LinkedChange>();
        foreach (string deploymentId in incident.LinkedDeploymentIds)
        {
            if (await correlation.GetChangeAsync(incident.TenantId, incident.EnvironmentId, deploymentId, cancellationToken) is { } change)
            {
                latest.Add(change);
            }
        }

        if (latest.Count > 0)
        {
            await SendAsync(incidentId, new UpdateChangesCommand(latest, LinkNew: false), cancellationToken);
        }

        RuntimeOptions settings = options.Value;
        CommandReply decided = await SendAsync(
            incidentId,
            new DecideCommand(DecisionId.From(StepInvestigationId(incidentId, incident.Cycle, "decide").Value), settings.Consensus.Enabled, settings.ConsensusSettings()),
            cancellationToken);
        IncidentSnapshot result = decided.Snapshot;
        logger.LogInformation(
            "Incident {IncidentId} cycle {Cycle} decided {Decision} at confidence {Confidence:F2}, leading {LeadingKey}: {Leading}",
            incidentId, result.Cycle, result.Decision, result.DecisionConfidence, result.LeadingKey, result.LeadingStatement);
        return result;
    }

    /// <summary>
    /// Turns a diagnosis into a typed plan (Phase 06): a diagnosed deployment is rolled back one
    /// revision, a diagnosed saturation caused by a batch service pauses the batch, and a diagnosed
    /// poison message on a queue consumer's queue is quarantined (Phase E01). Each plan
    /// states its preconditions, expected effect, blast radius and compensation, and carries
    /// Executor's plan-time policy evaluation. Any other diagnosis is escalated with its explanation.
    /// </summary>
    public async Task<IncidentSnapshot> PlanAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        IncidentSnapshot incident = await RequireAsync(incidentId, cancellationToken);
        if (incident.Decision != DecisionResult.Diagnosed || incident.PlanId is not null || incident.State != IncidentState.Investigating)
        {
            return incident;
        }

        RuntimeOptions settings = options.Value;
        var planId = PlanId.From(StepInvestigationId(incidentId, incident.Cycle, "plan").Value);
        var actionId = ActionId.From(StepInvestigationId(incidentId, incident.Cycle, "plan:action").Value);
        RemediationPlan? draft = incident switch
        {
            { LeadingChange: { ChangeKind: "deploy" or "rollback", PreviousVersion: { } previous } change } => new RemediationPlan
            {
                PlanId = planId,
                PlanVersion = 1,
                PlanHash = string.Empty,
                HypothesisId = incident.LeadingHypothesisId!.Value,
                Actions = [new RollbackAction { ActionId = actionId, TargetServiceId = change.ServiceId, FromRevision = change.Version, ToRevision = previous }],
                ExpectedRecovery = settings.RecoveryCriteriaFor(incident.AffectedServiceId),
                ObservationWindow = settings.ObservationWindow,
                Risk = RiskLevel.Medium,
                Preconditions = [new PlanPrecondition(change.ServiceId, "serving-revision", change.Version)],
                ExpectedEffect = $"{change.ServiceId} serves {previous} again, and {incident.AffectedServiceId}'s recovery signals return within {settings.ObservationWindow.TotalSeconds:F0} s.",
                BlastRadius = [.. new[] { change.ServiceId, incident.AffectedServiceId }.Distinct()],
                Compensation = $"None automatic: undoing the rollback would redeploy {change.ServiceId} {change.Version}, which is a human decision."
            },
            { LeadingKey: { } key, LeadingServiceId: { } consumer } when key.StartsWith(PoisonMessagePrefix, StringComparison.Ordinal) && settings.QueueServices.TryGetValue(consumer.Value, out string? queue) => new RemediationPlan
            {
                PlanId = planId,
                PlanVersion = 1,
                PlanHash = string.Empty,
                HypothesisId = incident.LeadingHypothesisId!.Value,
                Actions = [new QuarantineMessageAction { ActionId = actionId, TargetServiceId = consumer, Queue = queue, MinimumReceives = PoisonReceives, MaxMessages = PoisonMessagesAtMost, Reason = $"It fails on every receive: {incident.LeadingStatement}" }],
                ExpectedRecovery = settings.RecoveryCriteriaFor(incident.AffectedServiceId),
                ObservationWindow = settings.ObservationWindow,
                Risk = RiskLevel.Low,
                Preconditions = [new PlanPrecondition(consumer, "queue-state", "poisoned")],
                ExpectedEffect = $"The messages of {queue} received at least {PoisonReceives} times leave the queue, and {incident.AffectedServiceId}'s recovery signals return.",
                BlastRadius = [consumer],
                Compensation = $"Release the quarantined messages back to {queue} once they can be processed."
            },
            { LeadingServiceId: { } culprit } when settings.BatchServices.Contains(culprit.Value) => new RemediationPlan
            {
                PlanId = planId,
                PlanVersion = 1,
                PlanHash = string.Empty,
                HypothesisId = incident.LeadingHypothesisId!.Value,
                Actions = [new PauseBatchAction { ActionId = actionId, TargetServiceId = culprit, Reason = $"Its batch saturated a shared dependency: {incident.LeadingStatement}" }],
                ExpectedRecovery = settings.RecoveryCriteriaFor(incident.AffectedServiceId),
                ObservationWindow = settings.ObservationWindow,
                Risk = RiskLevel.Low,
                Preconditions = [new PlanPrecondition(culprit, "batch-active", "active")],
                ExpectedEffect = $"{culprit} releases its connections, and {incident.AffectedServiceId}'s recovery signals return.",
                BlastRadius = [culprit],
                Compensation = $"Resume the {culprit} batch once the incident is resolved."
            },
            _ => null
        };

        DateTimeOffset now = time.GetUtcNow();
        if (draft is null)
        {
            logger.LogInformation("Incident {IncidentId} has no automated remediation for {LeadingKey}; escalating", incidentId, incident.LeadingKey);
            return (await SendAsync(incidentId, new EscalateCommand(
                $"Diagnosed: {incident.LeadingStatement} No automated remediation exists for this cause; responders should act on it.",
                WithoutPlan: true), cancellationToken)).Snapshot;
        }

        RemediationPlan plan = draft with { PlanHash = RemediationPlanHash.Compute(draft) };
        PolicyEvaluation evaluation = await policy.EvaluateAsync(
            new PolicyEvaluationRequest(incident.TenantId, incident.EnvironmentId, incident.IncidentId, plan, incident.DecisionConfidence, incident.Severity.ToContract()),
            cancellationToken);
        RemediationAction action = plan.Actions[0];
        logger.LogInformation(
            "Incident {IncidentId} proposes {Action} on {Target}; policy {PolicyVersion} says {Policy}: {Trace}",
            incidentId, action.GetType().Name, action.TargetServiceId, evaluation.PolicyVersion, evaluation.Decision, string.Join("; ", evaluation.RuleTrace));
        return (await SendAsync(incidentId, new ProposePlanCommand(new ProposedPlan
        {
            PlanId = plan.PlanId,
            Version = plan.PlanVersion,
            Hash = plan.PlanHash,
            HypothesisId = plan.HypothesisId,
            TargetServiceId = action.TargetServiceId,
            ObservationWindow = plan.ObservationWindow,
            Document = JsonSerializer.Serialize(plan, SentinelJson.Options),
            Policy = evaluation.Decision.ToDomain(),
            PolicyVersion = evaluation.PolicyVersion,
            PolicyTrace = evaluation.RuleTrace,
            ProposedAt = now,
            ApprovalDeadline = evaluation.Decision == PolicyDecision.ApprovalRequired ? now + settings.Workflow.ApprovalTimeout : null,
            Summary = action switch
            {
                RollbackAction rollback => $"roll {rollback.TargetServiceId} back from {rollback.FromRevision} to {rollback.ToRevision}",
                PauseBatchAction pause => $"pause the {pause.TargetServiceId} batch ({plan.ExpectedEffect})",
                QuarantineMessageAction quarantine => $"quarantine the messages of {quarantine.TargetServiceId}'s {quarantine.Queue} queue received at least {quarantine.MinimumReceives} times",
                _ => plan.ExpectedEffect
            }
        }), cancellationToken)).Snapshot;
    }

    public async Task<IncidentSnapshot> EscalateAsync(IncidentId incidentId, string reason, CancellationToken cancellationToken) =>
        (await SendAsync(incidentId, new EscalateCommand(reason), cancellationToken)).Snapshot;

    /// <summary>Asks Executor to run the approved plan; the request's stream version is the fencing token.</summary>
    public async Task<IncidentSnapshot> RequestExecutionAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        IncidentSnapshot incident = await RequireAsync(incidentId, cancellationToken);
        if (!incident.Approved || incident.FencingToken is not null || incident.State != IncidentState.AwaitingApproval)
        {
            return incident;
        }

        return (await SendAsync(incidentId, new RequestExecutionCommand(), cancellationToken)).Snapshot;
    }

    public async Task<IncidentSnapshot> ScheduleRecoveryAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        IncidentSnapshot incident = await RequireAsync(incidentId, cancellationToken);
        if (incident.Execution != ExecutionResult.ActionVerified || incident.RecoveryWindowTo is not null)
        {
            return incident;
        }

        return (await SendAsync(incidentId, new ScheduleRecoveryCommand(), cancellationToken)).Snapshot;
    }

    /// <summary>
    /// Judges recovery. Until <paramref name="final"/>, telemetry that does not yet cover the window
    /// asks for a retry; on the final attempt it makes the result inconclusive, never recovered.
    /// </summary>
    public async Task<RecoveryStepResult> VerifyRecoveryAsync(IncidentId incidentId, bool final, CancellationToken cancellationToken)
    {
        IncidentSnapshot incident = await RequireAsync(incidentId, cancellationToken);
        if (incident.State != IncidentState.Verifying || incident.Recovery is not null)
        {
            return RecoveryStepResult.NotApplicable;
        }

        RecoveryAssessment assessment = await recovery.AssessAsync(incident, cancellationToken);
        if (!assessment.Trustworthy && !final)
        {
            return RecoveryStepResult.Retry;
        }

        RecoveryResult result = assessment.Trustworthy ? assessment.Result : RecoveryResult.Inconclusive;
        string? reason = assessment.Trustworthy ? assessment.Reason : $"Telemetry did not cover the window by the deadline: {assessment.Reason}";
        await SendAsync(incidentId, new RecordRecoveryCommand(result, assessment.Measurements, assessment.ObservedThrough, reason), cancellationToken);
        return RecoveryStepResult.Recorded;
    }

    private async Task<IncidentSnapshot> RequireAsync(IncidentId incidentId, CancellationToken cancellationToken) =>
        await owner.GetAsync(incidentId, cancellationToken) ?? throw new InvalidOperationException($"Incident {incidentId} does not exist.");

    private async Task<CommandReply> SendAsync(IncidentId incidentId, IncidentCommand command, CancellationToken cancellationToken)
    {
        Result<CommandReply> reply = await owner.SendAsync(incidentId, command, cancellationToken);
        if (reply.IsSuccess)
        {
            return reply.Value;
        }

        // A refused command is not retried: the stream already says what happened (ADR-004).
        logger.LogWarning("Incident {IncidentId} refused {Command}: {Reason}", incidentId, command.GetType().Name, reply.Error.Description);
        return new CommandReply(false, await RequireAsync(incidentId, cancellationToken));
    }

    private static string Objective(IncidentSnapshot incident, string role) => role switch
    {
        InvestigationRoles.Generalist => $"Determine the most likely cause of {incident.Title}, with symptoms from {incident.SymptomOnset:O}.",
        _ => $"As the {role} specialist, find the evidence in {role} that explains {incident.Title}, with symptoms from {incident.SymptomOnset:O}, and the hypotheses it supports or contradicts."
    };
}
