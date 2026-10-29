using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;

namespace Sentinel.Runtime.Application.BusinessProcesses.Remediation;

public sealed class ConfirmRemediationStartProcessor(ICommandDispatcher commands) : IProcessor<ConfirmRemediationStartRequest, RemediationStartResponse>
{
    public Task<Result<RemediationStartResponse>> Process(ConfirmRemediationStartRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new ConfirmRemediationStartCommand(request.TenantId, request.EnvironmentId, request.IncidentId, request.PlanId, request.PlanVersion, request.FencingToken), cancellationToken);
    }
}
