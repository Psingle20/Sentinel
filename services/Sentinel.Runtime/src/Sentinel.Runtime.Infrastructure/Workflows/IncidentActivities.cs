using Microsoft.Extensions.DependencyInjection;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application.Incidents;
using Temporalio.Activities;

namespace Sentinel.Runtime.Infrastructure.Workflows;

/// <summary>
/// The incident workflow's side-effect boundary (ADR-005): each activity runs one
/// <see cref="IncidentSteps"/> step in its own scope. Steps are idempotent, so Temporal's retries and
/// any duplicate execution after a worker crash append nothing twice (ADR-004).
/// </summary>
public sealed class IncidentActivities(IServiceScopeFactory scopes)
{
    [Activity("incident.get")]
    public Task<StepSummary> GetAsync(Guid incidentId) =>
        RunAsync(async (steps, token) => Summary(await steps.GetAsync(IncidentId.From(incidentId), token)
            ?? throw new InvalidOperationException($"Incident {incidentId} does not exist.")));

    [Activity("incident.request-investigations")]
    public Task<IReadOnlyList<Guid>> RequestInvestigationsAsync(Guid incidentId) =>
        RunAsync<IReadOnlyList<Guid>>(async (steps, token) => [.. (await steps.RequestInvestigationsAsync(IncidentId.From(incidentId), token)).Select(id => id.Value)]);

    [Activity("incident.request-challenge")]
    public Task<Guid?> RequestChallengeAsync(Guid incidentId) =>
        RunAsync(async (steps, token) => (await steps.RequestChallengeAsync(IncidentId.From(incidentId), token))?.Value);

    [Activity("incident.request-challenge-after-escalation")]
    public Task<Guid?> RequestChallengeAfterEscalationAsync(Guid incidentId) =>
        RunAsync(async (steps, token) => (await steps.RequestChallengeAsync(IncidentId.From(incidentId), token, afterEscalation: true))?.Value);

    [Activity("incident.time-out-investigations")]
    public Task TimeOutInvestigationsAsync(Guid incidentId, IReadOnlyList<Guid> investigationIds) =>
        RunAsync(async (steps, token) =>
        {
            await steps.TimeOutAsync(IncidentId.From(incidentId), [.. investigationIds.Select(InvestigationId.From)], token);
            return true;
        });

    [Activity("incident.decide")]
    public Task<StepSummary> DecideAsync(Guid incidentId) =>
        RunAsync(async (steps, token) => Summary(await steps.DecideAsync(IncidentId.From(incidentId), token)));

    [Activity("incident.plan")]
    public Task<StepSummary> PlanAsync(Guid incidentId) =>
        RunAsync(async (steps, token) => Summary(await steps.PlanAsync(IncidentId.From(incidentId), token)));

    [Activity("incident.escalate")]
    public Task<StepSummary> EscalateAsync(Guid incidentId, string reason) =>
        RunAsync(async (steps, token) => Summary(await steps.EscalateAsync(IncidentId.From(incidentId), reason, token)));

    [Activity("incident.request-execution")]
    public Task<StepSummary> RequestExecutionAsync(Guid incidentId) =>
        RunAsync(async (steps, token) => Summary(await steps.RequestExecutionAsync(IncidentId.From(incidentId), token)));

    [Activity("incident.schedule-recovery")]
    public Task<StepSummary> ScheduleRecoveryAsync(Guid incidentId) =>
        RunAsync(async (steps, token) => Summary(await steps.ScheduleRecoveryAsync(IncidentId.From(incidentId), token)));

    [Activity("incident.verify-recovery")]
    public Task<string> VerifyRecoveryAsync(Guid incidentId, bool final) =>
        RunAsync(async (steps, token) => (await steps.VerifyRecoveryAsync(IncidentId.From(incidentId), final, token)).ToString());

    public static StepSummary Summary(IncidentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new StepSummary(
            snapshot.State.ToString(),
            snapshot.Decision?.ToString(),
            snapshot.Approved,
            snapshot.Rejected,
            snapshot.Execution?.ToString(),
            snapshot.RecoveryWindowTo?.UtcDateTime,
            [.. snapshot.PendingInvestigationIds.Select(id => id.Value)],
            [.. snapshot.ReportedInvestigationIds.Select(id => id.Value)],
            snapshot.EscalationId?.Value);
    }

    private async Task<T> RunAsync<T>(Func<IncidentSteps, CancellationToken, Task<T>> step)
    {
        CancellationToken token = ActivityExecutionContext.HasCurrent ? ActivityExecutionContext.Current.CancellationToken : CancellationToken.None;
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        return await step(scope.ServiceProvider.GetRequiredService<IncidentSteps>(), token);
    }
}
