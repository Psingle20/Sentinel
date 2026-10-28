namespace CleanArchitecture.BuildingBlocks.Messaging;

public interface IQueryDispatcher
{
    Task<Result<TResult>> Dispatch<TResult>(IQuery<TResult> query, CancellationToken cancellationToken);
}
