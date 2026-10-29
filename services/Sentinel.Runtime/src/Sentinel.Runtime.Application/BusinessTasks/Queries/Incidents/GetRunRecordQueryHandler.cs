using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Incidents;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Memory;
using Sentinel.Runtime.Application.Options;
using Sentinel.Runtime.Application.Projections;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.BusinessTasks.Queries.Incidents;

/// <summary>The schema-2 run record: the projection, the version manifest and the incident's memory.</summary>
public sealed class GetRunRecordQueryHandler(IIncidentStore store, IIncidentMemoryStore memory, TimeProvider time, IOptions<RuntimeOptions> options) : IQueryHandler<GetRunRecordQuery, IncidentRunRecord?>
{
    public async Task<Result<IncidentRunRecord?>> Handle(GetRunRecordQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<StoredEvent> history = await store.ReadStreamAsync(query.IncidentId, cancellationToken);
        if (history.Count == 0)
        {
            return Result.Success<IncidentRunRecord?>(null);
        }

        var incident = Incident.Rehydrate(history.Select(stored => stored.Event));
        return Result.Success<IncidentRunRecord?>(new IncidentRunRecord(
            IncidentRunRecord.CurrentSchemaVersion,
            IncidentProjector.Project(history),
            time.GetUtcNow(),
            IncidentMemoryBuilder.Versions(incident, options.Value.Memory.RedactionPolicyVersion, query.Dataset),
            await memory.GetAsync(query.IncidentId, cancellationToken)));
    }
}
