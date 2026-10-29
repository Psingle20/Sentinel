using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Application;
using Sentinel.Runtime.Application.Incidents;
using Sentinel.Runtime.Application.Memory;
using Sentinel.Runtime.Domain.Incidents;
using Sentinel.Runtime.Infrastructure.Persistence;
using Shouldly;
using static Sentinel.Runtime.Tests.IncidentScenario;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Incidents.Commands;
using Sentinel.Runtime.Application.Options;
using Sentinel.Runtime.Domain.Incidents.Events;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Runtime.Application.BusinessProcesses.Incidents;
using Sentinel.Runtime.Application.BusinessProcesses.Memory;
using Sentinel.Runtime.Application.BusinessTasks.Queries.Incidents;

namespace Sentinel.Runtime.Tests;

/// <summary>Phase 07 on Runtime's side: incident memory, similar-incident retrieval, retention, domain replay and postmortems.</summary>
public sealed class MemoryReplayTests : IDisposable
{
    private static readonly TenantId Globex = TenantId.From("globex");

    private readonly InMemoryRuntime _runtime = new();

    public void Dispose() => _runtime.Dispose();

    [Fact]
    public async Task A_resolved_incident_is_remembered_with_its_cause_action_outcome_versions_and_retention()
    {
        IncidentId incidentId = await ResolveAsync(InMemoryRuntime.Tenant, "a");

        IncidentMemoryView memory = _runtime.Memory.Records.Single(record => record.IncidentId == incidentId);
        memory.Status.ShouldBe(IncidentStatus.Resolved);
        memory.Symptoms.ShouldContain(new SymptomSignature(Checkout, "slo_burn_rate"));
        memory.Cause.ShouldNotBeNull().ServiceId.ShouldBe(Checkout);
        memory.Action.ShouldNotBeNull().Kind.ShouldBe("rollback");
        memory.Action.RecoveryOutcome.ShouldBe(nameof(RecoveryOutcome.Recovered));
        memory.Versions.Models.ShouldContain(model => model.PromptVersion == "investigator-v1");
        memory.Versions.ToolCatalogVersions.ShouldBe(["tools-v2"]);
        memory.Versions.PolicyVersion.ShouldBe("policy-test");
        memory.Retention.RetainUntil.ShouldBe(_runtime.Time.GetUtcNow() + TimeSpan.FromDays(400));
        memory.Retention.LegalHold.ShouldBeFalse();
        memory.Features.ShouldContain("cause:change:a");
        memory.Features.ShouldContain("outcome:resolved");
        memory.EvidenceIds.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task A_new_investigation_gets_similar_incidents_of_its_own_tenant_only_as_hints_with_provenance()
    {
        IncidentId remembered = await ResolveAsync(InMemoryRuntime.Tenant, "a");
        await ResolveAsync(Globex, "g");

        IncidentId current = await OpenAsync(InMemoryRuntime.Tenant, "b");
        await InScope(steps => steps.RequestInvestigationsAsync(current, CancellationToken.None));

        InvestigateIncident command = _runtime.Outbox.Of<InvestigateIncident>().First(message => message.IncidentId == current);
        SimilarIncident hint = command.SimilarIncidents.ShouldNotBeNull().ShouldHaveSingleItem();
        hint.IncidentId.ShouldBe(remembered);
        hint.SharedFeatures.ShouldContain("symptom:checkout-service/slo_burn_rate");
        hint.SharedFeatures.ShouldAllBe(feature => MemoryFeatures.IsObservable(feature));
        hint.CauseKey.ShouldBe("change:a");
        hint.Similarity.ShouldBe(1);

        // The hint is part of the stream, so the investigation's inputs replay exactly.
        IReadOnlyList<StoredEvent> history = await _runtime.Store.ReadStreamAsync(current, CancellationToken.None);
        history.Select(stored => stored.Event).OfType<InvestigationRequested>().ShouldAllBe(requested => requested.SimilarIncidents!.Single().IncidentId == remembered);
    }

    [Fact]
    public async Task Expired_memory_is_not_retrieved_unless_on_legal_hold_and_the_purge_is_a_dry_run_by_default()
    {
        IncidentId held = await ResolveAsync(InMemoryRuntime.Tenant, "held");
        IncidentId expiring = await ResolveAsync(InMemoryRuntime.Tenant, "expiring");
        await RequestAsync(new SetLegalHoldRequest(held, true, "operator@example.test"));
        _runtime.Time.Advance(TimeSpan.FromDays(401));

        IncidentId current = await OpenAsync(InMemoryRuntime.Tenant, "now");
        IReadOnlyList<SimilarIncident> similar = (await RequestAsync(new GetSimilarIncidentsRequest(current))).Value!;
        similar.Select(match => match.IncidentId).ShouldBe([held]);

        PurgeReport dryRun = (await RequestAsync(new PurgeIncidentMemoryRequest(true, "operator@example.test", 100))).Value;
        dryRun.IncidentIds.ShouldBe([expiring]);
        _runtime.Memory.Records.Single(record => record.IncidentId == expiring).Retention.DeletedAt.ShouldBeNull();

        PurgeReport purged = (await RequestAsync(new PurgeIncidentMemoryRequest(false, "operator@example.test", 100))).Value;
        purged.IncidentIds.ShouldBe([expiring]);
        IncidentMemoryView tombstone = _runtime.Memory.Records.Single(record => record.IncidentId == expiring);
        tombstone.Retention.DeletedAt.ShouldNotBeNull();
        tombstone.Features.ShouldBeEmpty();
        tombstone.Cause.ShouldBeNull();
    }

    [Fact]
    public async Task A_privacy_deletion_removes_memory_from_retrieval_and_later_events_never_restore_it()
    {
        IncidentId deleted = await OpenAsync(InMemoryRuntime.Tenant, "a");
        await InScope(steps => steps.RequestInvestigationsAsync(deleted, CancellationToken.None));
        await _runtime.Owner.SendAsync(deleted, new EscalateCommand("Responders take it from here."), CancellationToken.None);
        _runtime.Memory.Records.ShouldContain(record => record.IncidentId == deleted);

        (await RequestAsync(new DeleteIncidentMemoryRequest(deleted, "privacy@example.test", "Erasure request."))).IsSuccess.ShouldBeTrue();
        (await _runtime.Owner.SendAsync(deleted, new ResolveClearedCommand("The symptom cleared."), CancellationToken.None)).Value.Snapshot.State.ShouldBe(IncidentState.Resolved);

        IncidentMemoryView tombstone = _runtime.Memory.Records.Single(record => record.IncidentId == deleted);
        tombstone.Title.ShouldBe("[deleted]");
        tombstone.Retention.DeletedBy.ShouldBe("privacy@example.test");
        IncidentId current = await OpenAsync(InMemoryRuntime.Tenant, "b");
        (await RequestAsync(new GetSimilarIncidentsRequest(current))).Value!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Memory_on_legal_hold_cannot_be_deleted()
    {
        IncidentId held = await ResolveAsync(InMemoryRuntime.Tenant, "a");
        await RequestAsync(new SetLegalHoldRequest(held, true, "operator@example.test"));

        (await RequestAsync(new DeleteIncidentMemoryRequest(held, "privacy@example.test", "Erasure request."))).Error.ShouldBe(MemoryErrors.LegalHold);
    }

    [Fact]
    public async Task Domain_replay_rebuilds_the_same_state_and_decisions_without_any_side_effect()
    {
        IncidentId incidentId = await ResolveAsync(InMemoryRuntime.Tenant, "a");
        int messages = _runtime.Outbox.Messages.Count;
        int appends = _runtime.Store.AppendCount;
        int signals = _runtime.Workflows.Signals.Count;
        int evaluations = _runtime.Policy.Calls;
        int memory = _runtime.Memory.Records.Count;

        DomainReplayResult replay = (await RequestAsync(new ReplayDomainRequest(incidentId))).Value!;

        replay.Level.ShouldBe(ReplayLevel.Domain);
        replay.Deterministic.ShouldBeTrue();
        replay.StateStable.ShouldBeTrue();
        replay.ProjectionMatches.ShouldBeTrue(string.Join("; ", replay.ProjectionDifferences));
        replay.Decisions.ShouldHaveSingleItem().Matches.ShouldBeTrue();
        _runtime.Outbox.Messages.Count.ShouldBe(messages);
        _runtime.Store.AppendCount.ShouldBe(appends);
        _runtime.Workflows.Signals.Count.ShouldBe(signals);
        _runtime.Policy.Calls.ShouldBe(evaluations);
        _runtime.Memory.Records.Count.ShouldBe(memory);
    }

    [Fact]
    public async Task Domain_replay_reports_a_projection_that_drifted_from_its_stream()
    {
        IncidentId incidentId = await ResolveAsync(InMemoryRuntime.Tenant, "a");
        IncidentDetail stored = (await _runtime.Store.GetDetailAsync(incidentId, CancellationToken.None))!;
        await _runtime.Store.SaveProjectionAsync(stored with { Summary = stored.Summary with { Title = "edited by hand" } }, isOpen: false, CancellationToken.None);

        DomainReplayResult replay = (await RequestAsync(new ReplayDomainRequest(incidentId))).Value!;

        replay.ProjectionMatches.ShouldBeFalse();
        replay.ProjectionDifferences.ShouldContain(difference => difference.StartsWith("$.summary.title", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_consensus_decision_records_its_settings_and_replays_identically_after_the_configuration_changes()
    {
        IncidentId incidentId = await OpenAsync(InMemoryRuntime.Tenant, "a");
        IReadOnlyList<InvestigationId> investigations = await InScope(steps => steps.RequestInvestigationsAsync(incidentId, CancellationToken.None));
        await ReportAsync(incidentId, investigations, "a");
        var settings = new ConsensusSettings { SourceReliability = new Dictionary<string, double>(StringComparer.Ordinal) { ["surge"] = 0.9 } };
        await _runtime.Owner.SendAsync(incidentId, new DecideCommand(DecisionId.New(), UseConsensus: true, settings), CancellationToken.None);

        var changed = new RuntimeOptions();
        InMemoryRuntime.Configure(changed);
        changed.Consensus.DefaultReliability = 0.1;
        var replay = new ReplayDomainQueryHandler(_runtime.Store, _runtime.Time, Options.Create(changed));
        DomainReplayResult result = (await replay.Handle(new ReplayDomainQuery(incidentId), CancellationToken.None)).Value!;

        DecisionReplay decision = result.Decisions.ShouldHaveSingleItem();
        decision.SettingsRecorded.ShouldBeTrue();
        decision.AlgorithmVersion.ShouldBe(ConsensusSettings.AlgorithmVersion);
        decision.Matches.ShouldBeTrue();
        decision.ReplayedInputsHash.ShouldBe(decision.RecordedInputsHash);
    }

    [Fact]
    public async Task Every_postmortem_fact_traces_back_to_the_stream_or_the_evidence()
    {
        IncidentId incidentId = await ResolveAsync(InMemoryRuntime.Tenant, "a");

        Postmortem postmortem = (await RequestAsync(new GetPostmortemRequest(incidentId))).Value!;

        IReadOnlyList<StoredEvent> history = await _runtime.Store.ReadStreamAsync(incidentId, CancellationToken.None);
        HashSet<string> versions = [.. history.Select(stored => stored.Version.ToString(CultureInfo.InvariantCulture))];
        IncidentDetail detail = (await _runtime.Store.GetDetailAsync(incidentId, CancellationToken.None))!;
        HashSet<string> evidence = [.. detail.Evidence.Where(item => item.Status == EvidenceStatus.Accepted).Select(item => item.Finding.EvidenceId.ToString())];
        HashSet<string> observations = [.. detail.Evidence.SelectMany(item => item.Finding.SourceObservationIds).Select(id => id.ToString())];

        PostmortemFact[] facts = [postmortem.Summary, .. postmortem.Timeline, .. postmortem.Evidence, .. postmortem.Decision, .. postmortem.Actions, .. postmortem.Recovery, .. postmortem.OpenQuestions];
        facts.ShouldAllBe(fact => fact.Sources.Count > 0);
        foreach (SourceRef source in facts.SelectMany(fact => fact.Sources))
        {
            switch (source.Kind)
            {
                case "event":
                    versions.ShouldContain(source.Ref);
                    break;
                case "evidence":
                    evidence.ShouldContain(source.Ref);
                    break;
                case "observation":
                    observations.ShouldContain(source.Ref);
                    break;
            }
        }

        postmortem.Evidence.ShouldNotBeEmpty();
        postmortem.Actions.ShouldContain(fact => fact.Text.Contains("responder@example.test approved", StringComparison.Ordinal));
        postmortem.Recovery.ShouldContain(fact => fact.Text.StartsWith("Recovery Recovered", StringComparison.Ordinal));
        postmortem.Summary.Text.ShouldContain("Resolved");
        postmortem.Versions.PolicyVersion.ShouldBe("policy-test");
    }

    [Fact]
    public async Task The_run_record_is_schema_2_with_the_version_manifest_and_the_memory()
    {
        IncidentId incidentId = await ResolveAsync(InMemoryRuntime.Tenant, "a");

        IncidentRunRecord record = (await RequestAsync(new GetRunRecordRequest(incidentId, "scenarios-v1"))).Value!;

        record.SchemaVersion.ShouldBe(2);
        record.Versions.ShouldNotBeNull().Dataset.ShouldBe("scenarios-v1");
        record.Memory.ShouldNotBeNull().IncidentId.ShouldBe(incidentId);
    }

    [Fact]
    public void A_detection_stored_before_Phase_07_is_upcast_with_its_trigger_metric()
    {
        var legacy = new IncidentDetected(
            IncidentId.New(), InMemoryRuntime.Tenant, InMemoryRuntime.Environment, Checkout, "checkout-service slo_burn_rate at 14.8",
            IncidentSeverity.Critical, Start, [], [], Start);

        var upcast = (IncidentDetected)IncidentEventTypes.Deserialize("incident.detected.v1", IncidentEventTypes.Serialize(legacy));

        upcast.TriggerMetric.ShouldBe("slo_burn_rate");
        IncidentEventTypes.Deserialize("incident.detected.v1", IncidentEventTypes.Serialize(upcast with { TriggerMetric = "latency_p95" }))
            .ShouldBeOfType<IncidentDetected>().TriggerMetric.ShouldBe("latency_p95");
    }

    private async Task<IncidentId> OpenAsync(TenantId tenant, string run)
    {
        var incidentId = IncidentId.New();
        LinkedChange change = Deployment("checkout-service", "v2.4.1", "v2.4.0", -60, -40, run);
        await _runtime.Owner.SendAsync(incidentId, new OpenIncidentCommand(
            tenant, InMemoryRuntime.Environment, Checkout, "checkout-service slo_burn_rate at 9.0", IncidentSeverity.High, Start,
            [Sentinel.Contracts.Primitives.Identifiers.ObservationId.From($"prometheus:slo_burn_rate:checkout-service:{run}")], [change], "slo_burn_rate"), CancellationToken.None);
        return incidentId;
    }

    private async Task ReportAsync(IncidentId incidentId, IReadOnlyList<InvestigationId> investigations, string run)
    {
        var cause = HypothesisId.New();
        Hypothesis hypothesis = Hypothesis(cause, "The v2.4.1 deployment leaks Redis connections.", Checkout, Sentinel.Contracts.Primitives.Identifiers.ObservationId.From($"deploy-controller:deployment:{run}:started")) with { Key = $"change:{run}" };
        Evidence[] evidence =
        [
            Supporting(cause, 0.85, -30, 0, "surge-1", "surge-2") with { IndependenceGroup = "redis-connections" },
            Supporting(cause, 0.6, -40, -20, "deploy-1", "deploy-2") with { IndependenceGroup = "deployment" }
        ];
        await _runtime.Owner.SendAsync(incidentId, new RecordInvestigationCommand(Report(investigations[0], evidence, hypothesis, Hypothesis(HypothesisId.New(), "Load.")) with { ToolCatalogVersion = "tools-v2" }), CancellationToken.None);
        await _runtime.Owner.SendAsync(incidentId, new TimeOutInvestigationsCommand([.. investigations.Skip(1)]), CancellationToken.None);
    }

    /// <summary>Drives one incident from detection to a verified recovery through the real steps.</summary>
    private async Task<IncidentId> ResolveAsync(TenantId tenant, string run)
    {
        IncidentId incidentId = await OpenAsync(tenant, run);
        IReadOnlyList<InvestigationId> investigations = await InScope(steps => steps.RequestInvestigationsAsync(incidentId, CancellationToken.None));
        await ReportAsync(incidentId, investigations, run);
        (await InScope(steps => steps.DecideAsync(incidentId, CancellationToken.None))).Decision.ShouldBe(DecisionResult.Diagnosed);
        await InScope(steps => steps.PlanAsync(incidentId, CancellationToken.None));

        RemediationPlan plan = _runtime.Outbox.Of<RemediationPlanProposed>().Single(message => message.IncidentId == incidentId).Plan;
        await _runtime.Owner.SendAsync(incidentId, new ApprovePlanCommand(plan.PlanId, plan.PlanVersion, plan.PlanHash, "responder@example.test", "jws"), CancellationToken.None);
        await InScope(steps => steps.RequestExecutionAsync(incidentId, CancellationToken.None));
        long token = _runtime.Outbox.Of<ExecuteRemediation>().Single(message => message.IncidentId == incidentId).FencingToken;
        await _runtime.Owner.SendAsync(incidentId, new ConfirmStartCommand(tenant, InMemoryRuntime.Environment, plan.PlanId, plan.PlanVersion, token), CancellationToken.None);
        await _runtime.Owner.SendAsync(incidentId, new RecordExecutionCommand(plan.PlanId, plan.PlanVersion, ExecutionResult.ActionVerified, "Rolled back."), CancellationToken.None);
        await InScope(steps => steps.ScheduleRecoveryAsync(incidentId, CancellationToken.None));
        await InScope(steps => steps.VerifyRecoveryAsync(incidentId, final: true, CancellationToken.None));

        (await _runtime.Owner.GetAsync(incidentId, CancellationToken.None))!.State.ShouldBe(IncidentState.Resolved);
        return incidentId;
    }

    private Task<T> InScope<T>(Func<IncidentSteps, Task<T>> action) => InScope<IncidentSteps, T>(action);

    // Orleans also names a type IRequest.
    private Task<Result> RequestAsync(CleanArchitecture.BuildingBlocks.Messaging.IRequest request) =>
        _runtime.InScopeAsync(provider => provider.GetRequiredService<IRequestDispatcher>().HandleRequestAsync(request, CancellationToken.None));

    private Task<Result<TResult>> RequestAsync<TResult>(CleanArchitecture.BuildingBlocks.Messaging.IRequest<TResult> request) =>
        _runtime.InScopeAsync(provider => provider.GetRequiredService<IRequestDispatcher>().HandleRequestAsync(request, CancellationToken.None));

    private Task<T> InScope<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull =>
        _runtime.InScopeAsync(provider => action(provider.GetRequiredService<TService>()));
}
