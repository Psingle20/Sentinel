using Sentinel.Gateway.Domain.Approvals;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.Abstractions.Persistence;

/// <summary>The Gateway's approvals, one per plan version.</summary>
public interface IApprovalStore
{
    Task<Approval?> FindAsync(IncidentId incidentId, PlanId planId, int planVersion, CancellationToken cancellationToken);

    Task SaveAsync(Approval approval, CancellationToken cancellationToken);
}
