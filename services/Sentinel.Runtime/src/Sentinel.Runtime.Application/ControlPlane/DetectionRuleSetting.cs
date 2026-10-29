using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;

namespace Sentinel.Runtime.Application.ControlPlane;

/// <summary>An SLO signal that opens an incident: above the detection threshold for the required samples.</summary>
public sealed record DetectionRuleSetting(string Service, string Metric, double DetectionThreshold, double OnsetThreshold, int RequiredSamples, TimeSpan Lookback, TimeSpan ClearAfter);
