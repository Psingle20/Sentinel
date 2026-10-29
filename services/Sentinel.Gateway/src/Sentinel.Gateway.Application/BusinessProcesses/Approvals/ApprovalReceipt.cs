using Sentinel.Gateway.Domain.Approvals;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Gateway.Application.BusinessProcesses.Approvals;

/// <summary>What the approver gets back for a decision; a repeated decision gets the first receipt again.</summary>
public sealed record ApprovalReceipt(Guid ApprovalId, ApprovalDecision Decision, AttestationId? AttestationId, DateTimeOffset DecidedAt, DateTimeOffset? ExpiresAt);