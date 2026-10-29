using System.Text.Json;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Options;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Memory;
using Sentinel.Runtime.Application.Options;
using Sentinel.Runtime.Application.Projections;
using Sentinel.Runtime.Domain.Incidents;

namespace Sentinel.Runtime.Application.BusinessTasks.Queries.Incidents;

/// <summary>
/// Level-1 domain replay (Phase 07): rebuilds an incident's state and projection from its event
/// stream and re-runs every recorded decision on the stream prefix it was made from. It reads the
/// stream and the stored projection and nothing else: it writes no event, sends no message, signals
/// no workflow and calls no provider or model, so it can run against any incident at any time.
/// </summary>
public sealed class ReplayDomainQueryHandler(IIncidentStore store, TimeProvider time, IOptions<RuntimeOptions> options) : IQueryHandler<ReplayDomainQuery, DomainReplayResult?>
{
    public async Task<Result<DomainReplayResult?>> Handle(ReplayDomainQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IReadOnlyList<StoredEvent> history = await store.ReadStreamAsync(query.IncidentId, cancellationToken);
        if (history.Count == 0)
        {
            return Result.Success<DomainReplayResult?>(null);
        }

        string hash = DomainReplay.StateHash(Incident.Rehydrate(history.Select(stored => stored.Event)));
        bool stable = hash == DomainReplay.StateHash(Incident.Rehydrate(history.Select(stored => stored.Event)));

        IncidentDetail rebuilt = IncidentProjector.Project(history);
        IncidentDetail? stored = await store.GetDetailAsync(query.IncidentId, cancellationToken);
        IReadOnlyList<string> differences = stored is null
            ? ["No projection is stored for the incident."]
            : JsonDifferences.Between(JsonSerializer.SerializeToNode(stored, SentinelJson.Options), JsonSerializer.SerializeToNode(rebuilt, SentinelJson.Options), 25);

        return Result.Success<DomainReplayResult?>(new DomainReplayResult
        {
            IncidentId = query.IncidentId,
            StreamVersion = history[^1].Version,
            EventCount = history.Count,
            StateHash = hash,
            StateStable = stable,
            ProjectionMatches = differences.Count == 0,
            ProjectionDifferences = differences,
            Decisions = DomainReplay.Decisions(history, options.Value.ConsensusSettings()),
            ReplayedAt = time.GetUtcNow()
        });
    }
}
