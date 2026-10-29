using CleanArchitecture.BuildingBlocks;
using CleanArchitecture.BuildingBlocks.Integration;
using CleanArchitecture.BuildingBlocks.Messaging;
using Sentinel.Executor.Application.Abstractions.Persistence;

namespace Sentinel.Executor.Application.BusinessTasks.Commands.Controls;

public sealed class SetActionDisabledCommandHandler(IControlStore controls, IUnitOfWork unitOfWork) : ICommandHandler<SetActionDisabledCommand>
{
    public async Task<Result> Handle(SetActionDisabledCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        await controls.SetActionDisabledAsync(command.ActionKind, command.Disabled, command.Actor, cancellationToken);
        await unitOfWork.CommitAsync(cancellationToken);
        return Result.Success();
    }
}
