using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;

namespace Sentinel.Runtime.Application.ControlPlane;

public sealed record InvestigationSetting
{
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>Runs the adversarial verifier before deciding (Phase 05).</summary>
    public bool Challenge { get; init; }

    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(15);

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    public TimeSpan ReportGrace { get; init; } = TimeSpan.FromSeconds(30);

    public int MaxToolCalls { get; init; } = 12;

    public int MaxModelTokens { get; init; } = 40_000;
}
