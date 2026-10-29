using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Gateway.Application.Queries.Approvals;

public sealed record GetApprovalKeysQuery : IQuery<ApprovalKeySet>;