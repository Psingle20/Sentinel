using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;

namespace Sentinel.Runtime.Application.ControlPlane;

public sealed record ServiceCatalogSetting
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Dependencies { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyDictionary<string, string> Owners { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<string> BatchServices { get; init; } = [];

    public IReadOnlyDictionary<string, string> QueueServices { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> RouteServices { get; init; } = new Dictionary<string, string>();
}
