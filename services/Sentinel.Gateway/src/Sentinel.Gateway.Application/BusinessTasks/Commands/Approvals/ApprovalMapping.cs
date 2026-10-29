using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Gateway.Domain.Approvals;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Gateway.Application.BusinessProcesses.Approvals;

namespace Sentinel.Gateway.Application.BusinessTasks.Commands.Approvals;

/// <summary>Runtime's incident view to what an approval decision depends on, and approvals to receipts and message metadata.</summary>
internal static class ApprovalMapping
{
    public static IncidentState? State(IncidentDetail? incident) => incident is null
        ? null
        : new IncidentState(
            incident.Summary.TenantId,
            incident.Summary.EnvironmentId,
            incident.Summary.IncidentId,
            incident.Plan is { } plan
                ? new CurrentPlan(plan.Plan.PlanId, plan.Plan.PlanVersion, plan.Plan.PlanHash, RemediationPlanHash.Matches(plan.Plan), plan.Status.ToString(), plan.Status == PlanStatus.AwaitingApproval)
                : null,
            incident.Summary.Status == IncidentStatus.AwaitingApproval,
            incident.Execution?.StartedAt is not null);

    public static ApprovalReceipt Receipt(Approval approval) =>
        new(approval.ApprovalId, approval.Decision, approval.AttestationId, approval.DecidedAt, approval.ExpiresAt);

    public static MessageMetadata Metadata(IncidentDetail incident) => new(
        CorrelationId: incident.Summary.IncidentId.ToString(),
        Headers: SentinelHeaders.For(incident.Summary.TenantId, incident.Summary.EnvironmentId, incident.Summary.AffectedServiceId, incident.Summary.IncidentId));
}