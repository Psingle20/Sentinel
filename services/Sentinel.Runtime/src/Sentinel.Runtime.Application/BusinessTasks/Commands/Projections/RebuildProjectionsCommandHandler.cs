using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Projections;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Projections;

/// <summary>
/// Rebuilds every incident's read model from its stream (Phase 03 exit criterion: projections can be
/// deleted and rebuilt). It overwrites each projection in place, so it never needs to delete anything
/// and can run while Runtime serves traffic.
/// </summary>
public sealed class RebuildProjectionsCommandHandler(IIncidentStore incidents, IUnitOfWork unitOfWork, ILogger<RebuildProjectionsCommandHandler> logger) : ICommandHandler<RebuildProjectionsCommand, int>
{
    public async Task<Result<int>> Handle(RebuildProjectionsCommand command, CancellationToken cancellationToken)
    {
        int rebuilt = 0;
        foreach (IncidentId incidentId in await incidents.AllIncidentIdsAsync(cancellationToken))
        {
            IReadOnlyList<StoredEvent> history = await incidents.ReadStreamAsync(incidentId, cancellationToken);
            if (history.Count == 0)
            {
                continue;
            }

            IncidentDetail detail = IncidentProjector.Project(history);
            await incidents.SaveProjectionAsync(detail, detail.Summary.Status != IncidentStatus.Resolved, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            rebuilt++;
        }

        logger.LogInformation("Rebuilt {Count} incident projection(s) from their streams", rebuilt);
        return Result.Success(rebuilt);
    }
}
