using System.Collections.Concurrent;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Runtime.Domain.Incidents;
using ContractRecoveryEvaluated = Sentinel.Contracts.Incidents.RecoveryEvaluated;
using DomainRecoveryEvaluated = Sentinel.Runtime.Domain.Incidents.RecoveryEvaluated;

namespace Sentinel.Runtime.Application.Incidents;

/// <summary>An incident in memory with the stored events it was built from; what IncidentGrain keeps between commands.</summary>
public sealed record LoadedIncident(Incident Incident, IReadOnlyList<StoredEvent> History);

/// <summary>
/// Executes one command for the incident's single writer: load the stream (or use the writer's
/// cached copy), apply the command, append the new events with optimistic concurrency, refresh the
/// projection and enqueue the integration messages the events imply, then commit all of it together.
/// Messages exist only if the events do. Runs in its own scope and unit of work per command.
/// </summary>
public sealed class IncidentCommandExecutor(IIncidentStore store, IOutbox outbox, IUnitOfWork unitOfWork, Memory.IncidentMemoryRecorder memory, TimeProvider time, IOptions<RuntimeOptions> options)
{
    public async Task<(Result<CommandReply> Reply, LoadedIncident? Loaded)> ExecuteAsync(
        IncidentId incidentId,
        IncidentCommand command,
        LoadedIncident? cached,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        LoadedIncident? loaded = cached ?? await LoadAsync(incidentId, cancellationToken);
        IncidentState? before = loaded?.Incident.State;
        long version = loaded?.Incident.Version ?? 0;

        Result<Incident> applied = IncidentCommandProcessor.Apply(incidentId, loaded?.Incident, command, time.GetUtcNow());
        if (applied.IsFailure && loaded is null)
        {
            return (Result.Failure<CommandReply>(applied.Error), null);
        }

        // Events appended before a failing step of the same command are kept, as a partial success.
        Incident incident = applied.IsSuccess ? applied.Value : loaded!.Incident;
        IReadOnlyList<StoredEvent> history = loaded?.History ?? [];
        IReadOnlyList<StoredEvent> appended = await PersistAsync(incident, history, before, cancellationToken);
        var next = new LoadedIncident(incident, [.. history, .. appended]);
        var reply = new CommandReply(incident.Version > version, IncidentSnapshot.Of(incident));

        return (applied.IsSuccess ? Result.Success(reply) : Result.Failure<CommandReply>(applied.Error), next);
    }

    public async Task<LoadedIncident?> LoadAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        IReadOnlyList<StoredEvent> history = await store.ReadStreamAsync(incidentId, cancellationToken);
        return history.Count == 0 ? null : new LoadedIncident(Incident.Rehydrate(history.Select(stored => stored.Event)), history);
    }

    private async Task<IReadOnlyList<StoredEvent>> PersistAsync(Incident incident, IReadOnlyList<StoredEvent> history, IncidentState? before, CancellationToken cancellationToken)
    {
        IIncidentEvent[] pending = [.. incident.DequeueDomainEvents().Cast<IIncidentEvent>()];
        if (pending.Length == 0)
        {
            return [];
        }

        long expected = incident.Version - pending.Length;
        StoredEvent[] appended = [.. pending.Select((@event, index) => new StoredEvent(expected + index + 1, @event))];
        await store.AppendAsync(incident, expected, appended, cancellationToken);
        IncidentDetail detail = IncidentProjector.Project([.. history, .. appended]);
        await store.SaveProjectionAsync(detail, incident.IsOpen, cancellationToken);
        await memory.RecordAsync(incident, detail, time.GetUtcNow(), cancellationToken);

        RuntimeOptions settings = options.Value;
        IncidentMessages.Enqueue(outbox, incident, appended, before, settings.InvestigationBudget(), settings.DependenciesOf(incident.AffectedServiceId));
        await unitOfWork.CommitAsync(cancellationToken);
        return appended;
    }
}

/// <summary>
/// Serializes writers per incident inside one process. The in-process single writer used by tests
/// and by tooling that runs without the actor runtime; a running Runtime uses IncidentGrain.
/// </summary>
public sealed class IncidentLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Release(gate);
    }

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

/// <summary>The in-process <see cref="IIncidentOwner"/>: a lock per incident, a fresh read per command, one retry on a concurrency conflict.</summary>
public sealed class LocalIncidentOwner(IServiceScopeFactory scopes, IncidentLocks locks) : IIncidentOwner
{
    public async Task<Result<CommandReply>> SendAsync(IncidentId incidentId, IncidentCommand command, CancellationToken cancellationToken)
    {
        using IDisposable held = await locks.AcquireAsync($"incident:{incidentId}", cancellationToken);
        int attempt = 0;
        while (true)
        {
            attempt++;
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            try
            {
                return (await scope.ServiceProvider.GetRequiredService<IncidentCommandExecutor>().ExecuteAsync(incidentId, command, null, cancellationToken)).Reply;
            }
            catch (IncidentConcurrencyException) when (attempt < 3)
            {
                // Another writer appended first; the command is data, so it is applied again to a fresh read.
            }
        }
    }

    public async Task<IncidentSnapshot?> GetAsync(IncidentId incidentId, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        LoadedIncident? loaded = await scope.ServiceProvider.GetRequiredService<IncidentCommandExecutor>().LoadAsync(incidentId, cancellationToken);
        return loaded is null ? null : IncidentSnapshot.Of(loaded.Incident);
    }
}

/// <summary>Maps new stream events to the integration messages other services depend on (ADR-012).</summary>
public static class IncidentMessages
{
    public static void Enqueue(
        IOutbox outbox,
        Incident incident,
        IReadOnlyList<StoredEvent> appended,
        IncidentState? stateBefore,
        InvestigationBudget budget,
        IReadOnlyList<ServiceId> dependencies)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(incident);
        ArgumentNullException.ThrowIfNull(appended);

        var metadata = new MessageMetadata(
            CorrelationId: incident.Id.ToString(),
            Headers: SentinelHeaders.For(incident.TenantId, incident.EnvironmentId, incident.AffectedServiceId, incident.Id));

        foreach (StoredEvent stored in appended)
        {
            switch (stored.Event)
            {
                case IncidentDetected detected:
                    outbox.Enqueue(new IncidentOpened
                    {
                        TenantId = incident.TenantId,
                        EnvironmentId = incident.EnvironmentId,
                        IncidentId = incident.Id,
                        AffectedServiceId = incident.AffectedServiceId,
                        Title = detected.Title,
                        Severity = detected.Severity.ToContract(),
                        OpenedAt = detected.OccurredAt,
                        TriggerObservationIds = detected.TriggerObservationIds,
                        LinkedDeploymentObservationIds = [.. detected.LinkedChanges.SelectMany(change => change.ObservationIds)],
                        StreamVersion = stored.Version
                    }, metadata);
                    break;
                case InvestigationRequested requested:
                    outbox.Enqueue(Investigate(incident, requested, budget, dependencies), metadata);
                    break;
                case InvestigationReported reported:
                    outbox.Enqueue(new IncidentBlackboardUpdated
                    {
                        TenantId = incident.TenantId,
                        EnvironmentId = incident.EnvironmentId,
                        IncidentId = incident.Id,
                        InvestigationId = reported.Report.InvestigationId,
                        Role = reported.Report.Role,
                        AcceptedEvidence = reported.Report.Evidence.Count - reported.RejectedEvidence.Count,
                        RejectedEvidence = reported.RejectedEvidence.Count,
                        HypothesisKeys = [.. incident.AllHypotheses().Select(hypothesis => hypothesis.Key ?? hypothesis.HypothesisId.ToString())],
                        UpdatedAt = reported.OccurredAt,
                        StreamVersion = stored.Version
                    }, metadata);
                    break;
                case DecisionMade made:
                    DecisionView view = made.Decision.ToView();
                    outbox.Enqueue(new DecisionRecorded
                    {
                        TenantId = incident.TenantId,
                        EnvironmentId = incident.EnvironmentId,
                        IncidentId = incident.Id,
                        DecisionId = view.DecisionId,
                        Outcome = view.Outcome,
                        LeadingHypothesisId = view.LeadingHypothesisId,
                        RejectedHypothesisIds = view.RejectedHypothesisIds,
                        Confidence = view.Confidence,
                        EvidenceCompleteness = view.EvidenceCompleteness,
                        Calibration = view.Calibration,
                        Rationale = view.Rationale,
                        DecidedAt = view.DecidedAt,
                        StreamVersion = stored.Version
                    }, metadata);
                    break;
                case PlanProposed proposed:
                    outbox.Enqueue(new RemediationPlanProposed
                    {
                        TenantId = incident.TenantId,
                        EnvironmentId = incident.EnvironmentId,
                        IncidentId = incident.Id,
                        Plan = proposed.Plan.PlanDocument(),
                        PolicyEvaluation = proposed.Plan.PolicyEvaluation(),
                        StreamVersion = stored.Version
                    }, metadata);
                    break;
                case ExecutionRequested requested:
                    outbox.Enqueue(new ExecuteRemediation
                    {
                        TenantId = incident.TenantId,
                        EnvironmentId = incident.EnvironmentId,
                        IncidentId = incident.Id,
                        Plan = incident.Plan!.PlanDocument(),
                        Attestation = incident.Approval!.Attestation,
                        FencingToken = requested.FencingToken,
                        IdempotencyKey = requested.IdempotencyKey,
                        RequestedAt = requested.OccurredAt
                    }, metadata);
                    break;
                case DomainRecoveryEvaluated evaluated:
                    outbox.Enqueue(new ContractRecoveryEvaluated
                    {
                        TenantId = incident.TenantId,
                        EnvironmentId = incident.EnvironmentId,
                        IncidentId = incident.Id,
                        PlanId = evaluated.PlanId,
                        Outcome = evaluated.Result.ToContract(),
                        Measurements = [.. evaluated.Measurements.Select(measurement => measurement.ToContract())],
                        ObservedThrough = evaluated.ObservedThrough,
                        EvaluatedAt = evaluated.OccurredAt,
                        StreamVersion = stored.Version
                    }, metadata);
                    break;
            }
        }

        if (stateBefore is not null && stateBefore != incident.State)
        {
            outbox.Enqueue(new IncidentStatusChanged
            {
                TenantId = incident.TenantId,
                EnvironmentId = incident.EnvironmentId,
                IncidentId = incident.Id,
                Status = incident.State.ToStatus(),
                Reason = incident.ClosingReason,
                ChangedAt = incident.UpdatedAt,
                StreamVersion = incident.Version
            }, metadata);
        }
    }

    private static InvestigateIncident Investigate(Incident incident, InvestigationRequested requested, InvestigationBudget budget, IReadOnlyList<ServiceId> dependencies)
    {
        bool verifier = requested.Role == InvestigationRoles.Verifier;
        HashSet<HypothesisId> challenged = [.. requested.ChallengedHypothesisIds ?? []];
        return new InvestigateIncident
        {
            TenantId = incident.TenantId,
            EnvironmentId = incident.EnvironmentId,
            IncidentId = incident.Id,
            InvestigationId = requested.InvestigationId,
            AffectedServiceId = incident.AffectedServiceId,
            // The catalog's dependencies, every service merged into the incident and any service with a correlated change.
            RelatedServiceIds = [.. dependencies
                .Concat(incident.AffectedServices)
                .Concat(incident.Changes.Select(change => change.ServiceId))
                .Where(service => service != incident.AffectedServiceId)
                .Distinct()],
            Objective = requested.Objective,
            ObservationWindow = new Sentinel.Contracts.TimeRange(requested.WindowFrom, requested.WindowTo),
            Budget = budget,
            SymptomOnset = incident.SymptomOnset,
            Role = requested.Role,
            Hypotheses = verifier ? [.. incident.AllHypotheses().Where(hypothesis => challenged.Contains(hypothesis.HypothesisId)).Select(hypothesis => hypothesis.ToContract())] : null,
            Evidence = verifier ? [.. incident.AcceptedEvidence().Select(evidence => evidence.ToContract())] : null,
            SimilarIncidents = requested.SimilarIncidents is { Count: > 0 } similar
                ? [.. similar.Select(prior => new SimilarIncident(prior.IncidentId, prior.Similarity, prior.SharedFeatures, prior.CauseKey, prior.CauseStatement, prior.Outcome, prior.RecordedAt))]
                : null
        };
    }
}
