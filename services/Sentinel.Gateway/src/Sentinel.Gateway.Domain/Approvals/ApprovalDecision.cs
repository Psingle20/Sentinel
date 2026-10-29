namespace Sentinel.Gateway.Domain.Approvals;

/// <summary>An approver's decision on one plan version, and the approval's state; <see cref="Approval"/> holds the transitions.</summary>
public enum ApprovalDecision
{
    Approved,
    Rejected,
    Revoked
}
