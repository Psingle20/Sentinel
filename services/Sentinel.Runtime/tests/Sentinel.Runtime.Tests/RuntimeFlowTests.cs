using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.BusinessProcesses.Remediation;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Domain.Incidents;
using Shouldly;
using ContractDirection = Sentinel.Contracts.Investigations.EvidenceDirection;
using Sentinel.Runtime.Application.Abstractions.Runtime;

namespace Sentinel.Runtime.Tests;

public sealed class RuntimeFlowTests : IDisposable
{
    private readonly InMemoryRuntime _runtime = new();

    public void Dispose() => _runtime.Dispose();

    [Fact]
    public async Task A_burst_of_burning_samples_opens_one_incident_starts_its_workflow_and_survives_redelivery()
    {
        await ObserveAsync(InMemoryRuntime.Burn(0, 3));
        await ObserveAsync(InMemoryRuntime.Burn(5, 4));
        await ObserveAsync(InMemoryRuntime.Burn(10, 6));
        await ObserveAsync(InMemoryRuntime.Burn(5, 4));

        IncidentSummary incident = (await _runtime.Store.ListAsync(10, CancellationToken.None)).ShouldHaveSingleItem();
        incident.SymptomOnset.ShouldBe(IncidentScenario.Start);
        _runtime.Workflows.Started.ShouldHaveSingleItem().Incident.ShouldBe(incident.IncidentId);
        _runtime.Outbox.Of<IncidentOpened>().ShouldHaveSingleItem();
    }

    /// <summary>
    /// S0, the healthy control: a logging change and a benign order-service deployment while
    /// checkout's SLO stays healthy apart from one isolated spike. Changes alone never open an
    /// incident, and one sample above the threshold is not the sustained burn detection requires.
    /// </summary>
    [Fact]
    public async Task The_healthy_control_opens_no_incident_and_starts_no_workflow()
    {
        await ObserveAsync(OrderChange("s0-config", "v1.7.0", "config-change", 0));
        await ObserveAsync(OrderChange("s0-deploy", "v1.7.1", "deploy", 30));
        double[] burn = [0.2, 0.4, 0.3, 2.6, 0.5, 0.3, 0.6, 0.2, 0.4, 0.3];
        for (int index = 0; index < burn.Length; index++)
        {
            await ObserveAsync(InMemoryRuntime.Burn(index * 15, burn[index], "s0-"));
        }

        (await _runtime.Store.ListAsync(10, CancellationToken.None)).ShouldBeEmpty();
        _runtime.Workflows.Started.ShouldBeEmpty();
        _runtime.Outbox.Of<IncidentOpened>().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_retried_open_with_the_same_trigger_is_the_same_incident()
    {
        MetricObservation trigger = InMemoryRuntime.Burn(5, 4);

        DetectionService.IncidentIdFor(trigger).ShouldBe(DetectionService.IncidentIdFor(trigger));
        DetectionService.IncidentIdFor(trigger).ShouldNotBe(DetectionService.IncidentIdFor(InMemoryRuntime.Burn(10, 4)));
    }

    [Fact]
    public async Task Duplicate_commands_append_nothing_new()
    {
        IncidentId incidentId = await OpenAsync();
        IReadOnlyList<InvestigationId> first = await StepsAsync(steps => steps.RequestInvestigationsAsync(incidentId, CancellationToken.None));
        int appends = _runtime.Store.AppendCount;

        IReadOnlyList<InvestigationId> again = await StepsAsync(steps => steps.RequestInvestigationsAsync(incidentId, CancellationToken.None));

        again.ShouldBe(first);
        _runtime.Store.AppendCount.ShouldBe(appends);
        _runtime.Outbox.Of<InvestigateIncident>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_commands_for_one_incident_are_serialized_without_gaps()
    {
        IncidentId incidentId = await OpenAsync();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(index => _runtime.Owner.SendAsync(
            incidentId,
            new LinkSignalCommand(ServiceId.From($"service-{index}"), "latency_p95", index, [ObservationId.From($"o-{index}")], 0.5, ["test"]),
            CancellationToken.None)));

        IReadOnlyList<StoredEvent> stream = await _runtime.Store.ReadStreamAsync(incidentId, CancellationToken.None);
        stream.Select(stored => stored.Version).ShouldBe(Enumerable.Range(1, stream.Count).Select(version => (long)version));
        stream.Count(stored => stored.Event is SignalLinked).ShouldBe(20);
    }

    [Fact]
    public async Task The_workflow_steps_run_the_golden_path_and_each_repeat_is_a_no_op()
    {
        IncidentId incidentId = await OpenAsync();
        await _runtime.Correlation.SaveChangeAsync(InMemoryRuntime.Tenant, InMemoryRuntime.Environment, Change(), CancellationToken.None);
        await _runtime.Owner.SendAsync(incidentId, new UpdateChangesCommand([Change()], LinkNew: true), CancellationToken.None);

        InvestigationId investigation = (await StepsAsync(steps => steps.RequestInvestigationsAsync(incidentId, CancellationToken.None))).ShouldHaveSingleItem();
        await ControlAsync(control => control.HandleInvestigationAsync(Report(incidentId, investigation), CancellationToken.None));
        await ControlAsync(control => control.HandleInvestigationAsync(Report(incidentId, investigation), CancellationToken.None));
        _runtime.Workflows.Signals.Count(signal => signal.Kind == IncidentSignalKind.InvestigationReported).ShouldBe(2);

        IncidentSnapshot decided = await StepsAsync(steps => steps.DecideAsync(incidentId, CancellationToken.None));
        (await StepsAsync(steps => steps.DecideAsync(incidentId, CancellationToken.None))).Version.ShouldBe(decided.Version);
        decided.Decision.ShouldBe(DecisionResult.Diagnosed);

        IncidentSnapshot planned = await StepsAsync(steps => steps.PlanAsync(incidentId, CancellationToken.None));
        await StepsAsync(steps => steps.PlanAsync(incidentId, CancellationToken.None));
        planned.State.ShouldBe(IncidentState.AwaitingApproval);
        _runtime.Policy.Calls.ShouldBe(1);
        RemediationPlan plan = _runtime.Outbox.Of<RemediationPlanProposed>().ShouldHaveSingleItem().Plan;

        await RequestAsync(Approval(incidentId, plan));
        await StepsAsync(steps => steps.RequestExecutionAsync(incidentId, CancellationToken.None));
        IncidentSnapshot executing = await StepsAsync(steps => steps.RequestExecutionAsync(incidentId, CancellationToken.None));
        ExecuteRemediation execute = _runtime.Outbox.Of<ExecuteRemediation>().ShouldHaveSingleItem();
        execute.FencingToken.ShouldBe(executing.FencingToken!.Value);

        await ControlAsync(control => control.ConfirmStartAsync(new RemediationStartRequest(InMemoryRuntime.Tenant, InMemoryRuntime.Environment, incidentId, plan.PlanId, plan.PlanVersion, execute.FencingToken), CancellationToken.None));
        await ControlAsync(control => control.HandleExecutedAsync(Executed(incidentId, plan), CancellationToken.None));
        IncidentSnapshot scheduled = await StepsAsync(steps => steps.ScheduleRecoveryAsync(incidentId, CancellationToken.None));
        scheduled.State.ShouldBe(IncidentState.Verifying);

        (await StepsAsync(steps => steps.VerifyRecoveryAsync(incidentId, final: false, CancellationToken.None))).ShouldBe(RecoveryStepResult.Recorded);
        (await StepsAsync(steps => steps.VerifyRecoveryAsync(incidentId, final: false, CancellationToken.None))).ShouldBe(RecoveryStepResult.NotApplicable);
        (await _runtime.Owner.GetAsync(incidentId, CancellationToken.None))!.State.ShouldBe(IncidentState.Resolved);
    }

    [Fact]
    public async Task A_start_confirmation_with_a_stale_fencing_token_is_refused()
    {
        IncidentId incidentId = await OpenAsync();

        RemediationStartResponse response = await ControlAsync(control => control.ConfirmStartAsync(
            new RemediationStartRequest(InMemoryRuntime.Tenant, InMemoryRuntime.Environment, incidentId, PlanId.New(), 1, 99), CancellationToken.None));

        response.Confirmed.ShouldBeFalse();
    }

    [Fact]
    public async Task Projections_deleted_and_rebuilt_from_the_streams_are_identical()
    {
        IncidentId incidentId = await OpenAsync();
        await StepsAsync(steps => steps.RequestInvestigationsAsync(incidentId, CancellationToken.None));
        string before = JsonSerializer.Serialize(await _runtime.Store.GetDetailAsync(incidentId, CancellationToken.None), SentinelJson.Options);

        _runtime.Store.DeleteProjections();
        int rebuilt = await _runtime.InScopeAsync(provider => provider.GetRequiredService<ProjectionRebuilder>().RebuildAsync(CancellationToken.None));

        rebuilt.ShouldBe(1);
        JsonSerializer.Serialize(await _runtime.Store.GetDetailAsync(incidentId, CancellationToken.None), SentinelJson.Options).ShouldBe(before);
    }

    [Fact]
    public async Task The_state_view_reports_how_far_the_projection_lags_the_stream()
    {
        IncidentId incidentId = await OpenAsync();

        Result<IncidentStateView> state = await _runtime.InScopeAsync(provider => provider.GetRequiredService<IncidentQueryService>().StateAsync(incidentId, CancellationToken.None));

        state.Value.ProjectionLag.ShouldBe(0);
        state.Value.StreamVersion.ShouldBe(state.Value.ProjectionVersion!.Value);
    }

    private async Task<IncidentId> OpenAsync()
    {
        await ObserveAsync(InMemoryRuntime.Burn(0, 3));
        await ObserveAsync(InMemoryRuntime.Burn(5, 4));
        return (await _runtime.Store.ListAsync(10, CancellationToken.None)).Single().IncidentId;
    }

    private Task<Result> ObserveAsync(Observation observation) =>
        _runtime.InScopeAsync(provider => provider.GetRequiredService<DetectionService>().HandleAsync(observation, CancellationToken.None));

    private Task<T> StepsAsync<T>(Func<IncidentSteps, Task<T>> step) =>
        _runtime.InScopeAsync(provider => step(provider.GetRequiredService<IncidentSteps>()));

    // Orleans also names a type IRequest.
    private Task<Result> RequestAsync(CleanArchitecture.BuildingBlocks.Messaging.IRequest request) =>
        _runtime.InScopeAsync(provider => provider.GetRequiredService<IRequestDispatcher>().HandleRequestAsync(request, CancellationToken.None));

    private Task<T> ControlAsync<T>(Func<RemediationControlService, Task<T>> step) =>
        _runtime.InScopeAsync(provider => step(provider.GetRequiredService<RemediationControlService>()));

    private static LinkedChange Change() => IncidentScenario.Deployment("checkout-service", "v2.4.1", "v2.4.0", -60, -40, "d1");

    private static DeploymentObservation OrderChange(string deploymentId, string version, string kind, int secondsFromStart) => new()
    {
        ObservationId = ObservationId.From($"deploy-controller:deployment:{deploymentId}:completed"),
        TenantId = InMemoryRuntime.Tenant,
        EnvironmentId = InMemoryRuntime.Environment,
        ServiceId = ServiceId.From("order-service"),
        SourceProvider = "deploy-controller",
        SourceResource = "deployments",
        EventTime = IncidentScenario.Start.AddSeconds(secondsFromStart),
        IngestTime = IncidentScenario.Start.AddSeconds(secondsFromStart + 5),
        DeploymentId = deploymentId,
        Version = version,
        PreviousVersion = "v1.7.0",
        ChangeKind = kind,
        StartedAt = IncidentScenario.Start.AddSeconds(secondsFromStart),
        CompletedAt = IncidentScenario.Start.AddSeconds(secondsFromStart + 20),
        Status = "completed"
    };

    private static InvestigationCompleted Report(IncidentId incidentId, InvestigationId investigationId)
    {
        var leak = HypothesisId.New();
        var other = HypothesisId.New();
        return new InvestigationCompleted
        {
            TenantId = InMemoryRuntime.Tenant,
            EnvironmentId = InMemoryRuntime.Environment,
            IncidentId = incidentId,
            InvestigationId = investigationId,
            AgentExecutionId = AgentExecutionId.New(),
            Outcome = InvestigationOutcome.Completed,
            Hypotheses =
            [
                new HypothesisFinding { HypothesisId = leak, Statement = "The checkout v2.4.1 deployment leaks Redis connections.", CausalChain = ["deploy", "leak", "burn"], SuspectedChangeObservationId = Change().ObservationIds[0] },
                new HypothesisFinding { HypothesisId = other, Statement = "Load changed.", CausalChain = ["load", "burn"] }
            ],
            Evidence =
            [
                new EvidenceFinding
                {
                    EvidenceId = EvidenceId.New(),
                    Statement = "Connections rose after the deploy.",
                    Direction = ContractDirection.Supports,
                    Strength = 0.85,
                    SourceObservationIds = [ObservationId.From("prometheus:redis_connections:checkout-service:1"), ObservationId.From("prometheus:redis_connections:checkout-service:2")],
                    HypothesisIds = [leak],
                    EventTimeRange = new Sentinel.Contracts.TimeRange(IncidentScenario.Start.AddSeconds(-30), IncidentScenario.Start),
                    ToolCallId = "call_redis_connections"
                }
            ],
            Model = new ModelMetadata("sentinel-test-double", "deterministic-analyst", "1", "investigator-v1"),
            CompletedAt = IncidentScenario.Start.AddMinutes(2)
        };
    }

    private static RecordApprovalRequest Approval(IncidentId incidentId, RemediationPlan plan) =>
        new(incidentId, plan.PlanId, plan.PlanVersion, plan.PlanHash, "responder@example.test", "header.claims.signature");

    private static RemediationExecuted Executed(IncidentId incidentId, RemediationPlan plan) => new()
    {
        TenantId = InMemoryRuntime.Tenant,
        EnvironmentId = InMemoryRuntime.Environment,
        IncidentId = incidentId,
        PlanId = plan.PlanId,
        PlanVersion = plan.PlanVersion,
        Outcome = ExecutionOutcome.ActionVerified,
        Detail = "Rolled back.",
        CompletedAt = IncidentScenario.Start.AddMinutes(4)
    };
}
