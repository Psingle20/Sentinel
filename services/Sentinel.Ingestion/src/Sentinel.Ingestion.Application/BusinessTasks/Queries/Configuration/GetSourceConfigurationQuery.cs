using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Ingestion.Application.ControlPlane;

namespace Sentinel.Ingestion.Application.BusinessTasks.Queries.Configuration;

public sealed record GetSourceConfigurationQuery : IQuery<DocumentState<SourceConfigurationDocument>>;
