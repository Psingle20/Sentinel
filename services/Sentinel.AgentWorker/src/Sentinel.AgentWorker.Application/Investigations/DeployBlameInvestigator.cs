using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sentinel.AgentWorker.Application.Tools;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;
using Sentinel.AgentWorker.Application.Abstractions.Providers;

namespace Sentinel.AgentWorker.Application.Investigations;

/// <summary>
/// The deploy-blame baseline (Phase 01, reference-system/scenarios/baseline/deploy-blame.md): blame
/// the latest deployment to the affected service or its dependencies inside the lookback window,
/// or abstain. It reads deployments only, so it cannot tell a cause from a coincidence.
/// </summary>
public sealed class DeployBlameInvestigator(IObservationQueries observations, TimeProvider time) : IInvestigator
{
    public const string InvestigatorName = "deploy-blame";
    public static readonly TimeSpan Lookback = TimeSpan.FromMinutes(10);
    private static readonly ExecutionModel Code = new("sentinel", InvestigatorName, "1", "deploy-blame-v1");

    public string Name => InvestigatorName;

    public async Task<InvestigationRun> InvestigateAsync(InvestigateIncident command, bool shadow, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset started = time.GetUtcNow();
        DateTimeOffset detectedAt = command.ObservationWindow.To;
        var range = new TimeRange(detectedAt - Lookback, detectedAt);
        ObservationQueryResult result = await observations.QueryAsync(new ObservationQuery
        {
            TenantId = command.TenantId,
            EnvironmentId = command.EnvironmentId,
            Kind = ObservationKind.Deployment,
            Range = range,
            Limit = 500
        }, cancellationToken);

        HashSet<ServiceId> scope = [command.AffectedServiceId, .. command.RelatedServiceIds];
        DeploymentObservation[] deployments = [.. result.Observations.OfType<DeploymentObservation>()
            .Where(deployment => scope.Contains(deployment.ServiceId) && deployment.ChangeKind is "deploy" or "rollback")];
        var toolCall = new ToolCallRecord
        {
            ToolCallId = "baseline-deployments",
            ToolName = RecentDeploymentsTool.Name,
            Arguments = JsonSerializer.Serialize(new { services = scope, from = range.From, to = range.To }, SentinelJson.Options),
            Result = JsonSerializer.Serialize(deployments.Select(deployment => deployment.ObservationId), SentinelJson.Options),
            ObservationIds = [.. deployments.Select(deployment => deployment.ObservationId)],
            StartedAt = started,
            Duration = time.GetUtcNow() - started
        };

        DeploymentObservation? blamed = deployments
            .GroupBy(deployment => deployment.DeploymentId)
            .Select(group => group.OrderBy(deployment => deployment.EventTime).First())
            .OrderByDescending(deployment => deployment.StartedAt)
            .ThenBy(deployment => deployment.ServiceId.Value, StringComparer.Ordinal)
            .FirstOrDefault();

        if (blamed is null)
        {
            return Record(command, shadow, started, toolCall, InvestigationOutcome.Abstained, "No deployment to the service or its dependencies in the lookback window.", [], []);
        }

        ObservationId[] cited = [.. deployments.Where(deployment => deployment.DeploymentId == blamed.DeploymentId).Select(deployment => deployment.ObservationId)];
        var blame = HypothesisId.New();
        var recency = EvidenceId.New();
        EvidenceFinding evidence = new()
        {
            EvidenceId = recency,
            Statement = $"{blamed.ServiceId} {blamed.Version} is the most recent deployment before detection, started at {blamed.StartedAt:O}.",
            Direction = EvidenceDirection.Supports,
            Strength = 0.6,
            SourceObservationIds = cited,
            HypothesisIds = [blame],
            EventTimeRange = new TimeRange(blamed.StartedAt, blamed.CompletedAt ?? blamed.StartedAt),
            ToolCallId = toolCall.ToolCallId,
            IndependenceGroup = $"deployment:{blamed.DeploymentId}"
        };
        HypothesisFinding[] hypotheses =
        [
            new()
            {
                HypothesisId = blame,
                Statement = $"The {blamed.ServiceId} {blamed.Version} deployment caused the symptoms.",
                CausalChain = [$"deploy {blamed.ServiceId} {blamed.Version}", "regression", "SLO burn"],
                SuspectedServiceId = blamed.ServiceId,
                SuspectedChangeObservationId = blamed.ObservationId,
                SupportingEvidenceIds = [recency]
            },
            new()
            {
                HypothesisId = HypothesisId.New(),
                Statement = "The symptoms have a cause other than a deployment.",
                CausalChain = ["unknown cause", "SLO burn"]
            }
        ];

        return Record(command, shadow, started, toolCall, InvestigationOutcome.Completed, null, [evidence], hypotheses);
    }

    // The baseline makes its one read and no model call, so its execution records just that read.
    private InvestigationRun Record(
        InvestigateIncident command,
        bool shadow,
        DateTimeOffset started,
        ToolCallRecord toolCall,
        InvestigationOutcome outcome,
        string? reason,
        IReadOnlyList<EvidenceFinding> evidence,
        IReadOnlyList<HypothesisFinding> hypotheses)
    {
        AgentExecution execution = InvestigationRecords.Start(command, Name, shadow, Code, started);
        execution.RecordToolCall(toolCall);
        InvestigationRecords.Finish(execution, outcome, reason, time.GetUtcNow());
        return InvestigationRecords.Build(command, execution, evidence, hypotheses);
    }
}
