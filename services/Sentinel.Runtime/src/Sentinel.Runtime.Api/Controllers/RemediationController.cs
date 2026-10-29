using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application;

namespace Sentinel.Runtime.Api.Controllers;

/// <summary>Executor's start confirmation, immediately before any side effect (ADR-006).</summary>
[Route(Routes.Root + "/remediation")]
public sealed class RemediationController(RemediationControlService control) : ApiController
{
    [HttpPost("start-confirmations")]
    public async Task<IResult> ConfirmStart(RemediationStartRequest request, CancellationToken cancellationToken) =>
        Results.Ok(await control.ConfirmStartAsync(request, cancellationToken));
}