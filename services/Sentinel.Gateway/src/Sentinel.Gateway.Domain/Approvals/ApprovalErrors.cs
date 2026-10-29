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

    /// <summary>Runtime, the authority on the incident, could not be asked; the revocation is not recorded, so the approval stands until it can be.</summary>
    public static readonly Error RuntimeUnavailable = Error.Failure("Approval.RuntimeUnavailable", "Runtime could not be reached to record the revocation; the approval still stands. Retry, or use the emergency stop.");

    public static Error NotAwaitingApproval(string planStatus) =>
        Error.Conflict("Approval.NotAwaitingApproval", $"The plan is {planStatus}, not awaiting approval.");

    /// <summary>Runtime refused the revocation: its reason is final, because Runtime's stream is the truth (ADR-004).</summary>
    public static Error RevocationRefused(string? reason) =>
        Error.Conflict("Approval.RevocationRefused", $"Runtime refused the revocation: {reason ?? "no reason given"}. The approval still stands.");
}
