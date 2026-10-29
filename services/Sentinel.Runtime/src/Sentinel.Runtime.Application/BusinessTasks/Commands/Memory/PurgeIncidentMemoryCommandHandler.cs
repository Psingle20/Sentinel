using System.Globalization;
using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Incidents;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Memory;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Memory;

/// <summary>
/// The retention purge. It is never scheduled: an operator runs it, a dry run first, and only
/// records past their retention and not on legal hold are deleted.
/// </summary>
public sealed class PurgeIncidentMemoryCommandHandler(IIncidentMemoryStore memory, IUnitOfWork unitOfWork, TimeProvider time, ILogger<PurgeIncidentMemoryCommandHandler> logger) : ICommandHandler<PurgeIncidentMemoryCommand, PurgeReport>
{
    public async Task<Result<PurgeReport>> Handle(PurgeIncidentMemoryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        DateTimeOffset now = time.GetUtcNow();
        IReadOnlyList<IncidentMemoryView> expired = await memory.ExpiredAsync(now, Math.Clamp(command.Limit, 1, 1000), cancellationToken);
        if (!command.DryRun)
        {
            foreach (IncidentMemoryView record in expired)
            {
                await memory.DeleteAsync(IncidentMemoryBuilder.Tombstone(record, command.Actor, string.Create(CultureInfo.InvariantCulture, $"Retention expired at {record.Retention.RetainUntil:O}."), now), cancellationToken);
            }

            logger.LogInformation("{Actor} purged {Count} expired incident memory record(s)", command.Actor, expired.Count);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success(new PurgeReport(command.DryRun, [.. expired.Select(record => record.IncidentId)], now));
    }
}
