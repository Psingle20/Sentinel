using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;

namespace Sentinel.Runtime.Application.ControlPlane;

public sealed record ConsensusSetting
{
    public bool Enabled { get; init; }

    public IReadOnlyDictionary<string, double> SourceReliability { get; init; } = new Dictionary<string, double>();

    public double DefaultReliability { get; init; } = 0.8;

    public double PartialWeight { get; init; } = 0.6;

    public double ContradictionWeight { get; init; } = 1.2;

    public double AbstainBelowConfidence { get; init; } = 0.5;

    public int MinimumIndependentSupport { get; init; } = 2;

    public double DissentMargin { get; init; } = 0.25;
}
