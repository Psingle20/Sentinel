using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Sentinel.Runtime.Application.Abstractions.Persistence;
using Sentinel.Runtime.Application.Memory;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Memory;

/// <summary>Deletes one incident's memory for privacy: the record becomes a tombstone and leaves every derived index. A legal hold refuses it.</summary>
public sealed class DeleteIncidentMemoryCommandHandler(IIncidentMemoryStore memory, IUnitOfWork unitOfWork, TimeProvider time, ILogger<DeleteIncidentMemoryCommandHandler> logger) : ICommandHandler<DeleteIncidentMemoryCommand>
{
    public async Task<Result> Handle(DeleteIncidentMemoryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result deleted = await DeleteAsync(command, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return deleted;
    }

    private async Task<Result> DeleteAsync(DeleteIncidentMemoryCommand command, CancellationToken cancellationToken)
    {
        if (await memory.GetAsync(command.IncidentId, cancellationToken) is not { } remembered)
        {
            return Result.Failure(MemoryErrors.NotRemembered);
        }

        if (remembered.Retention.LegalHold)
        {
            return Result.Failure(MemoryErrors.LegalHold);
        }

        if (remembered.Retention.DeletedAt is null)
        {
            await memory.DeleteAsync(IncidentMemoryBuilder.Tombstone(remembered, command.Actor, command.Reason, time.GetUtcNow()), cancellationToken);
            logger.LogInformation("{Actor} deleted incident {IncidentId} memory: {Reason}", command.Actor, command.IncidentId, command.Reason);
        }

        return Result.Success();
    }
}
