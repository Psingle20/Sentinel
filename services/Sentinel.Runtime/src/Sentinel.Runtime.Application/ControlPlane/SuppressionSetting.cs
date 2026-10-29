using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;

namespace Sentinel.Runtime.Application.ControlPlane;

public sealed record SuppressionSetting(string? Service, string? Metric, DateTimeOffset From, DateTimeOffset Until, string Reason);
