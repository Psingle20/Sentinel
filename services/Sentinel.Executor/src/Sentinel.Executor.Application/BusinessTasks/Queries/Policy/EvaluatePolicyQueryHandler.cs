using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application.Services;

namespace Sentinel.Executor.Application.BusinessTasks.Queries.Policy;

/// <summary>The advisory plan-time verdict, from the same policy engine that is authoritative at execution time.</summary>
public sealed class EvaluatePolicyQueryHandler(PolicyService policy) : IQueryHandler<EvaluatePolicyQuery, PolicyEvaluation>
{
    public async Task<Result<PolicyEvaluation>> Handle(EvaluatePolicyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return Result.Success(await policy.EvaluateAsync(query.Evaluation, cancellationToken));
    }
}
