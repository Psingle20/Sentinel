using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.Abstractions.Persistence;

namespace Sentinel.Executor.Application.BusinessTasks.Commands.Controls;

/// <summary>Stored in Executor's own database, so an emergency stop survives restarts and needs no other service.</summary>
public sealed class SetEmergencyStopCommandHandler(IControlStore controls, IUnitOfWork unitOfWork) : ICommandHandler<SetEmergencyStopCommand>
{
    public async Task<Result> Handle(SetEmergencyStopCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await controls.SetEmergencyStopAsync(command.Engaged, command.Actor, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
