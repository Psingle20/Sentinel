using CleanArchitecture.BuildingBlocks.Messaging;

namespace Sentinel.Runtime.Application.BusinessProcesses.Projections;

/// <summary>An operator rebuilds every incident projection from its stream.</summary>
public sealed record RebuildProjectionsRequest : IRequest<int>;
