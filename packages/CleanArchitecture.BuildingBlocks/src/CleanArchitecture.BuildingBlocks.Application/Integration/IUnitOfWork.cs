namespace CleanArchitecture.BuildingBlocks.Integration;

/// <summary>
/// Commits the current scope's writes, including enqueued outbox messages, atomically.
/// A scope disposed without a commit is rolled back.
/// </summary>
public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken cancellationToken);
}
