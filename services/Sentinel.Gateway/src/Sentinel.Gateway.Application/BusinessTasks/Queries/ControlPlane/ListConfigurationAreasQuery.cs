using CleanArchitecture.BuildingBlocks.Messaging;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;

public sealed record ListConfigurationAreasQuery : IQuery<IReadOnlyList<string>>;
