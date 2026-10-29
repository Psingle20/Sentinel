using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Security;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.Approvals;

/// <summary>The verification keys of every attestation that can still be valid; public keys only.</summary>
public sealed class GetApprovalKeysQueryHandler(IApprovalSigner signer) : IQueryHandler<GetApprovalKeysQuery, ApprovalKeySet>
{
    public async Task<Result<ApprovalKeySet>> Handle(GetApprovalKeysQuery query, CancellationToken cancellationToken) =>
        Result.Success(await signer.PublishedKeysAsync(cancellationToken));
}
