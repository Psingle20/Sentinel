using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;

namespace Sentinel.Runtime.Application.ControlPlane;

public sealed record RecoverySignalSetting(string? Service, string Metric, ThresholdComparison Comparison, double Threshold);
