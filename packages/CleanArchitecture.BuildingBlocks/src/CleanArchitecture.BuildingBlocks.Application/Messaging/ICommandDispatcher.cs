namespace CleanArchitecture.BuildingBlocks.Messaging;

public interface ICommandDispatcher
{
    Task<Result<TResult>> Dispatch<TResult>(ICommand<TResult> command, CancellationToken cancellationToken);

    Task<Result> Dispatch(ICommand command, CancellationToken cancellationToken);
}
