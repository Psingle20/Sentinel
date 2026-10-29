using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Gateway.Application.Abstractions.Persistence;

namespace Sentinel.Gateway.Application.ControlPlane;

/// <summary>
/// Control-plane changes (Phase 08) go to the service that owns the setting, which validates them; each
/// is audited here whether it succeeded or was refused.
/// </summary>
public sealed class ControlPlaneAudit(IAuditLog audit, IUnitOfWork unitOfWork, TimeProvider time)
{
    public async Task RecordAsync(string actor, string action, string target, object detail, CancellationToken cancellationToken)
    {
        await audit.RecordAsync(actor, action, target, detail, time.GetUtcNow(), cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
