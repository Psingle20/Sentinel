using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Microsoft.Extensions.Logging;
using Sentinel.Contracts.Incidents;
using Sentinel.Runtime.Application.Abstractions.Persistence;

namespace Sentinel.Runtime.Application.BusinessTasks.Commands.Memory;

/// <summary>Places or lifts a legal hold, which keeps a record past its retention and refuses its deletion.</summary>
public sealed class SetLegalHoldCommandHandler(IIncidentMemoryStore memory, IUnitOfWork unitOfWork, ILogger<SetLegalHoldCommandHandler> logger) : ICommandHandler<SetLegalHoldCommand, IncidentMemoryView?>
{
    public async Task<Result<IncidentMemoryView?>> Handle(SetLegalHoldCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        IncidentMemoryView? updated = null;
        if (await memory.GetAsync(command.IncidentId, cancellationToken) is { Retention.DeletedAt: null } remembered)
        {
            updated = remembered with { Retention = remembered.Retention with { LegalHold = command.Hold } };
            await memory.SaveAsync(updated, cancellationToken);
            logger.LogInformation("{Actor} set the legal hold of incident {IncidentId} memory to {Hold}", command.Actor, command.IncidentId, command.Hold);
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success(updated);
    }
}
