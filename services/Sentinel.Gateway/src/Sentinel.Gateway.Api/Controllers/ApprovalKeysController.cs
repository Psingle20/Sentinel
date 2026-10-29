using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Gateway.Application.BusinessProcesses.Approvals;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Gateway.Api.Controllers;

/// <summary>Approval verification keys, published for Executor (ADR-006). Public keys only, so no authentication.</summary>
public sealed class ApprovalKeysController(IRequestDispatcher requests) : ApiController
{
    [HttpGet(ApprovalAttestation.KeysPath)]
    public async Task<IResult> Get(CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new GetApprovalKeysRequest(), cancellationToken));
}