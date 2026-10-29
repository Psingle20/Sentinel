using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Ingestion.Application.BusinessTasks.Queries.Configuration;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Ingestion.Application.ControlPlane;

namespace Sentinel.Ingestion.Application.BusinessProcesses.Configuration;

public sealed class GetSourceConfigurationProcessor(IQueryDispatcher queries) : IProcessor<GetSourceConfigurationRequest, DocumentState<SourceConfigurationDocument>>
{
    public Task<Result<DocumentState<SourceConfigurationDocument>>> Process(GetSourceConfigurationRequest request, CancellationToken cancellationToken)
    {
        return queries.Dispatch(new GetSourceConfigurationQuery(), cancellationToken);
    }
}
