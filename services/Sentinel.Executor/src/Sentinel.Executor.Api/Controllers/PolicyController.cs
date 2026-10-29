using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application.BusinessProcesses.Policy;

namespace Sentinel.Executor.Api.Controllers;

/// <summary>Advisory plan-time policy evaluation for Runtime (ADR-006), and the current policy document with its history.</summary>
[Route(Routes.Root + "/policy")]
public sealed class PolicyController(IRequestDispatcher requests) : ApiController
{
    [HttpPost("evaluations")]
    public async Task<IResult> Evaluate(PolicyEvaluationRequest request, CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new EvaluatePolicyRequest(request), cancellationToken));

    [HttpGet]
    public async Task<IResult> Current(CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new GetPolicyRequest(), cancellationToken));

    [HttpGet("history")]
    public async Task<IResult> History(CancellationToken cancellationToken) =>
        Match(await requests.HandleRequestAsync(new GetPolicyHistoryRequest(), cancellationToken));
}
