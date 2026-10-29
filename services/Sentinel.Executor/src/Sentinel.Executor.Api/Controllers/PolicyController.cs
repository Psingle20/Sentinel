using Microsoft.AspNetCore.Mvc;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;

namespace Sentinel.Executor.Api.Controllers;

/// <summary>Advisory plan-time policy evaluation for Runtime (ADR-006), and the current policy document with its history.</summary>
[Route(Routes.Root + "/policy")]
public sealed class PolicyController(PolicyService policy, IPolicyStore policies) : ApiController
{
    [HttpPost("evaluations")]
    public async Task<IResult> Evaluate(PolicyEvaluationRequest request, CancellationToken cancellationToken) =>
        Results.Ok(await policy.EvaluateAsync(request, cancellationToken));

    [HttpGet]
    public async Task<IResult> Current(CancellationToken cancellationToken) =>
        Results.Ok(await policies.CurrentAsync(cancellationToken));

    [HttpGet("history")]
    public async Task<IResult> History(CancellationToken cancellationToken) =>
        Results.Ok(await policies.HistoryAsync(cancellationToken));
}
