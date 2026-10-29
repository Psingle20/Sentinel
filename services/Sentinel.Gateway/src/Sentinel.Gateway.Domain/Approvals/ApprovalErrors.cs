using CleanArchitecture.BuildingBlocks;

namespace Sentinel.Gateway.Domain.Approvals;

/// <summary>Why an approval, rejection or revocation was refused, with codes the public API keeps stable.</summary>
public static class ApprovalErrors
{
    public static readonly Error IncidentNotFound = Error.NotFound("Approval.IncidentNotFound", "The incident does not exist.");

    public static readonly Error NotCurrentPlan = Error.Conflict("Approval.NotCurrentPlan", "The plan is not the incident's current plan version; review the current plan.");

    public static readonly Error HashMismatch = Error.Conflict("Approval.HashMismatch", "The plan content changed since it was reviewed; review the current plan.");

    public static readonly Error AlreadyDecided = Error.Conflict("Approval.AlreadyDecided", "This plan version already has a different decision.");

    public static readonly Error ReasonRequired = Error.Problem("Approval.ReasonRequired", "A rejection or revocation needs a reason.");

    public static readonly Error NothingToRevoke = Error.Conflict("Approval.NothingToRevoke", "There is no approval of this plan version to revoke.");

    public static readonly Error ExecutionStarted = Error.Conflict("Approval.ExecutionStarted", "Execution already started; the approval can no longer be revoked. Use the emergency stop.");

    public static Error NotAwaitingApproval(string planStatus) =>
        Error.Conflict("Approval.NotAwaitingApproval", $"The plan is {planStatus}, not awaiting approval.");
}
