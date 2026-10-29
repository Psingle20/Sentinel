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

/// <summary>Builds the postmortem of an incident from its stream.</summary>
public sealed class GetPostmortemQueryHandler(IIncidentStore store, TimeProvider time, IOptions<RuntimeOptions> options) : IQueryHandler<GetPostmortemQuery, Postmortem?>
{
    public async Task<Result<Postmortem?>> Handle(GetPostmortemQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<StoredEvent> history = await store.ReadStreamAsync(query.IncidentId, cancellationToken);
        if (history.Count == 0)
        {
            return Result.Success<Postmortem?>(null);
        }

        var incident = Incident.Rehydrate(history.Select(stored => stored.Event));
        IncidentDetail detail = IncidentProjector.Project(history);
        return Result.Success<Postmortem?>(PostmortemBuilder.Build(history, detail, IncidentMemoryBuilder.Versions(incident, options.Value.Memory.RedactionPolicyVersion, dataset: null), time.GetUtcNow()));
    }
}
