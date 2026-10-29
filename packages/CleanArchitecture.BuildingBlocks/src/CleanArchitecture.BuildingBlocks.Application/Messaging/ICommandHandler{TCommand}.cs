namespace CleanArchitecture.BuildingBlocks.Messaging;

public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    Task<Result> Handle(TCommand command, CancellationToken cancellationToken);
}
