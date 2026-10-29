using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using CleanArchitecture.BuildingBlocks.Integration;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Tests;

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int Commits { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Commits++;
        return Task.CompletedTask;
    }
}
