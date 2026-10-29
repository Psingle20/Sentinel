using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Gateway.Application.Abstractions.Providers;

namespace Sentinel.Gateway.Application.BusinessTasks.Queries.ControlPlane;

public sealed record GetConfigurationQuery(string Area) : IQuery<ControlPlaneResponse>;
