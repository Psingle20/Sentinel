using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Configuration;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;

namespace Sentinel.Runtime.Application.ControlPlane;

public sealed record WorkflowSetting
{
    public TimeSpan ApprovalTimeout { get; init; } = TimeSpan.FromMinutes(30);

    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How long an outcome Executor could not determine is waited on before escalating; zero escalates at once.</summary>
    public TimeSpan ReconciliationTimeout { get; init; } = TimeSpan.FromMinutes(30);

    public TimeSpan ObservationWindow { get; init; } = TimeSpan.FromSeconds(90);

    public TimeSpan EvaluationLag { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan EvaluationTimeout { get; init; } = TimeSpan.FromMinutes(3);
}
