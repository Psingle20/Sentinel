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
using DomainRecoveryEvaluated = Sentinel.Runtime.Domain.Incidents.Events.RecoveryEvaluated;
using Sentinel.Runtime.Application.Abstractions.Runtime;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Incidents.Commands;
using Sentinel.Runtime.Application.Options;
using Sentinel.Runtime.Application.Projections;
using Sentinel.Runtime.Domain.Incidents.Events;

namespace Sentinel.Runtime.Application.Incidents;

/// <summary>
/// Executes one command for the incident's single writer: load the stream (or use the writer's
/// cached copy), apply the command, append the new events with optimistic concurrency, refresh the
/// projection and enqueue the integration messages the events imply, then commit all of it together.
/// Messages exist only if the events do. Runs in its own scope and unit of work per command.
/// </summary>
public sealed class IncidentCommandExecutor(IIncidentStore store, IOutbox outbox, IUnitOfWork unitOfWork, Memory.IncidentMemoryRecorder memory, Diagnostics.RuntimeTelemetry telemetry, TimeProvider time, IOptions<RuntimeOptions> options)
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
        telemetry.Committed(incident, appended);
        return appended;
    }
}
