using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.AgentWorker.Application.ControlPlane;

namespace Sentinel.AgentWorker.Application.BusinessTasks.Queries.Configuration;

public sealed record GetModelConfigurationQuery : IQuery<DocumentState<ModelConfigurationDocument>>;
