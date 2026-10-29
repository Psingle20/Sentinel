using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.AgentWorker.Application.BusinessTasks.Queries.Configuration;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.AgentWorker.Application.ControlPlane;

namespace Sentinel.AgentWorker.Application.BusinessProcesses.Configuration;

public sealed class GetModelConfigurationProcessor(IQueryDispatcher queries) : IProcessor<GetModelConfigurationRequest, DocumentState<ModelConfigurationDocument>>
{
    public Task<Result<DocumentState<ModelConfigurationDocument>>> Process(GetModelConfigurationRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetModelConfigurationQuery(), cancellationToken);
    }
}
