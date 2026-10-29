using CleanArchitecture.BuildingBlocks.Messaging;

namespace Sentinel.Gateway.Application.BusinessProcesses.ControlPlane;

/// <summary>The configuration areas and, by name, the service that owns each (Phase 08).</summary>
public sealed record ListConfigurationAreasRequest : IRequest<IReadOnlyList<string>>;
