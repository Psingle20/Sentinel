namespace CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;

/// <summary>Prepares storage, such as creating tables, before the dispatcher and consumers start.</summary>
public interface IMessagingStoreInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
