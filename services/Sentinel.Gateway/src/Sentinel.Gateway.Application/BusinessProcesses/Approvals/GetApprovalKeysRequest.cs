using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Gateway.Application.BusinessProcesses.Approvals;

/// <summary>Executor asks for the public keys that verify approval attestations (ADR-006).</summary>
public sealed record GetApprovalKeysRequest : IRequest<ApprovalKeySet>;
