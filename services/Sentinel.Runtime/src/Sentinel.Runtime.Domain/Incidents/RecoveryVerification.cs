using CleanArchitecture.BuildingBlocks;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Domain.Incidents.Events;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// The incident's recovery verification (Phase 06), a child of <see cref="Incident"/>: the window
/// observed after the action was verified, and the verdict judged from telemetry over it.
/// </summary>
internal sealed class RecoveryVerification
{
    private static readonly Dictionary<RecoveryState, RecoveryState[]> Transitions = new()
    {
        [RecoveryState.NotScheduled] = [RecoveryState.Scheduled],
        [RecoveryState.Scheduled] = [RecoveryState.Evaluated],
        [RecoveryState.Evaluated] = []
    };

    public RecoveryScheduled? Window { get; private set; }

    public RecoveryEvaluated? Evaluation { get; private set; }

    public RecoveryState State
    {
        get
        {
            if (Window is null)
            {
                return RecoveryState.NotScheduled;
            }

            return Evaluation is not null && Evaluation.PlanId == Window.PlanId ? RecoveryState.Evaluated : RecoveryState.Scheduled;
        }
    }

    /// <summary>The window over the plan's observation period, starting now; once scheduled, the same window again.</summary>
    public Result<RecoveryScheduled> Schedule(Remediation remediation, DateTimeOffset now, IncidentState incident)
    {
        if (!CanBecome(RecoveryState.Scheduled))
        {
            return Result.Success(Window!);
        }

        if (remediation.ExecutionOutcome is not { Result: ExecutionResult.ActionVerified } || remediation.Plan is not { } plan)
        {
            return Result.Failure<RecoveryScheduled>(IncidentErrors.InvalidState(incident, "schedule recovery verification"));
        }

        return Result.Success(new RecoveryScheduled(plan.PlanId, now, now + plan.ObservationWindow, now));
    }

    /// <summary>
    /// The verdict, once, while the incident is verifying: recovered resolves the incident. Unchanged or degraded reopens it
    /// for a follow-up cycle when <paramref name="followUpTriggers"/> is given, since the action took effect and something
    /// else still holds the symptom; anything else, including a verdict that asks for compensation, escalates it.
    /// </summary>
    public Result<IReadOnlyList<IIncidentEvent>> Record(RecoveryResult result, IReadOnlyList<Measurement> measurements, DateTimeOffset observedThrough, string? reason, DateTimeOffset now, IncidentState incident, IReadOnlyList<ObservationId>? followUpTriggers = null)
    {
        // A verdict for the scheduled window is final.
        if (Window is not null && !CanBecome(RecoveryState.Evaluated))
        {
            return Result.Success<IReadOnlyList<IIncidentEvent>>([]);
        }

        if (!CanBecome(RecoveryState.Evaluated) || !IncidentStateMachine.Allows(incident, IncidentOperation.RecordRecovery))
        {
            return Result.Failure<IReadOnlyList<IIncidentEvent>>(IncidentErrors.InvalidState(incident, "record recovery"));
        }

        IIncidentEvent closing = (result, followUpTriggers) switch
        {
            (RecoveryResult.Recovered, _) => new IncidentResolved("Recovery verified from telemetry.", now),
            (RecoveryResult.Unchanged or RecoveryResult.Degraded, { } triggers) => new IncidentReopened(
                $"Recovery {result} although the action took effect: {reason} Sentinel investigates again, knowing what was tried.",
                triggers,
                observedThrough,
                now,
                Window!.PlanId),
            _ => new IncidentEscalated($"Recovery {result}: {reason}", now)
        };
        return Result.Success<IReadOnlyList<IIncidentEvent>>([new RecoveryEvaluated(Window!.PlanId, result, measurements, observedThrough, reason, now), closing]);
    }

    public void Apply(IIncidentEvent @event)
    {
        switch (@event)
        {
            case RecoveryScheduled scheduled:
                Window = scheduled;
                break;
            case RecoveryEvaluated evaluated:
                Evaluation = evaluated;
                break;
            case IncidentReopened:
                Window = null;
                Evaluation = null;
                break;
        }
    }

    private bool CanBecome(RecoveryState next) => Transitions[State].Contains(next);
}
