using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application.BusinessTasks.Commands.Remediation;

namespace Sentinel.Runtime.Application.BusinessProcesses.Remediation;

public sealed class RecordRevocationProcessor(ICommandDispatcher commands) : IProcessor<RecordRevocationRequest, RevocationResponse>
{
    public Task<Result<RevocationResponse>> Process(RecordRevocationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return commands.Dispatch(new RecordRevocationCommand(request.IncidentId, request.PlanId, request.PlanVersion, request.RevokedBy, request.Reason, request.Attestation), cancellationToken);
    }
}
