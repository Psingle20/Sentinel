using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.BusinessTasks.Queries.Policy;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Executor.Application.BusinessProcesses.Policy;

public sealed class EvaluatePolicyProcessor(IQueryDispatcher queries) : IProcessor<EvaluatePolicyRequest, PolicyEvaluation>
{
    public Task<Result<PolicyEvaluation>> Process(EvaluatePolicyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return queries.Dispatch(new EvaluatePolicyQuery(request.Evaluation), cancellationToken);
    }
}
