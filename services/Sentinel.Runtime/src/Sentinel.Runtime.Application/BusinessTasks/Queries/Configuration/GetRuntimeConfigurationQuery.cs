using CleanArchitecture.BuildingBlocks.Messaging;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Runtime.Application.ControlPlane;

namespace Sentinel.Runtime.Application.BusinessTasks.Queries.Configuration;

public sealed record GetRuntimeConfigurationQuery : IQuery<DocumentState<RuntimeConfigurationDocument>>;
