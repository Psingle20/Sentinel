using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Approvals;

public sealed record GetApprovalKeysQuery : IQuery<ApprovalKeySet>;