using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessTasks.Queries.Configuration;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Runtime.Application.ControlPlane;

namespace Sentinel.Runtime.Application.BusinessProcesses.Configuration;

public sealed class GetRuntimeConfigurationProcessor(IQueryDispatcher queries) : IProcessor<GetRuntimeConfigurationRequest, DocumentState<RuntimeConfigurationDocument>>
{
    public Task<Result<DocumentState<RuntimeConfigurationDocument>>> Process(GetRuntimeConfigurationRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetRuntimeConfigurationQuery(), cancellationToken);
    }
}
