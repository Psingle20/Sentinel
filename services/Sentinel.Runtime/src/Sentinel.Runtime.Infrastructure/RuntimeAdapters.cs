using System.Net.Http.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Queries;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;

namespace Sentinel.Runtime.Infrastructure;

/// <summary>Executor's plan-time policy evaluation, a bounded synchronous call (ADR-006, ADR-012).</summary>
internal sealed class ExecutorPolicyClient(HttpClient http) : IPolicyEvaluator
{
    public async Task<PolicyEvaluation> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("/v1/policy/evaluations", request, SentinelJson.Options, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PolicyEvaluation>(SentinelJson.Options, cancellationToken))!;
    }
}

/// <summary>Ingestion's observation query contract (ADR-007); Runtime holds no provider credentials.</summary>
internal sealed class IngestionQueryClient(HttpClient http) : IObservationQueries
{
    public async Task<ObservationQueryResult> QueryAsync(ObservationQuery query, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("/v1/observations/query", query, SentinelJson.Options, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ObservationQueryResult>(SentinelJson.Options, cancellationToken))!;
    }
}

/// <summary>Hands each observation to its correlation partition, where detection runs serialized.</summary>
public sealed class ObservationRecordedHandler(ICorrelationPartitions partitions) : IIntegrationMessageHandler<ObservationRecorded>
{
    public async Task<Result> HandleAsync(ObservationRecorded message, MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        await partitions.ObserveAsync(message.Observation, cancellationToken);
        return Result.Success();
    }
}

public sealed class InvestigationCompletedHandler(RemediationControlService control) : IIntegrationMessageHandler<InvestigationCompleted>
{
    public Task<Result> HandleAsync(InvestigationCompleted message, MessageEnvelope envelope, CancellationToken cancellationToken) =>
        control.HandleInvestigationAsync(message, cancellationToken);
}

public sealed class RecordApprovalHandler(RemediationControlService control) : IIntegrationMessageHandler<RecordApproval>
{
    public Task<Result> HandleAsync(RecordApproval message, MessageEnvelope envelope, CancellationToken cancellationToken) =>
        control.HandleApprovalAsync(message, cancellationToken);
}

public sealed class RecordRejectionHandler(RemediationControlService control) : IIntegrationMessageHandler<RecordRejection>
{
    public Task<Result> HandleAsync(RecordRejection message, MessageEnvelope envelope, CancellationToken cancellationToken) =>
        control.HandleRejectionAsync(message, cancellationToken);
}

public sealed class RecordRevocationHandler(RemediationControlService control) : IIntegrationMessageHandler<RecordRevocation>
{
    public Task<Result> HandleAsync(RecordRevocation message, MessageEnvelope envelope, CancellationToken cancellationToken) =>
        control.HandleRevocationAsync(message, cancellationToken);
}

public sealed class RemediationExecutedHandler(RemediationControlService control) : IIntegrationMessageHandler<RemediationExecuted>
{
    public Task<Result> HandleAsync(RemediationExecuted message, MessageEnvelope envelope, CancellationToken cancellationToken) =>
        control.HandleExecutedAsync(message, cancellationToken);
}

/// <summary>
/// Makes sure every open incident's cycle has its workflow, for example after Temporal was down
/// when the incident opened. Starting is idempotent, so this only repairs and never duplicates.
/// </summary>
internal sealed class WorkflowReconciler(IServiceScopeFactory scopes, IIncidentOwner owner, IIncidentWorkflows workflows, TimeProvider time, ILogger<WorkflowReconciler> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, time, stoppingToken);
            try
            {
                IReadOnlyList<IncidentSummary> incidents;
                await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
                {
                    incidents = await scope.ServiceProvider.GetRequiredService<IIncidentStore>().ListAsync(200, stoppingToken);
                }

                foreach (IncidentSummary summary in incidents.Where(incident => incident.Status is not (IncidentStatus.Resolved or IncidentStatus.Escalated)))
                {
                    if (await owner.GetAsync(summary.IncidentId, stoppingToken) is { IsOpen: true } snapshot)
                    {
                        await workflows.EnsureStartedAsync(snapshot, stoppingToken);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Workflow reconciliation failed; it is retried in {Interval}", Interval);
            }
        }
    }
}
