namespace CleanArchitecture.BuildingBlocks.Messaging;

/// <summary>
/// The one way inbound adapters reach the application layer (ADR-020): sends a request to the
/// processor registered for its type. Endpoints, consumers and activities never reference commands,
/// queries or handlers directly.
/// </summary>
public interface IRequestDispatcher
{
    Task<Result<TResult>> HandleRequestAsync<TResult>(IRequest<TResult> request, CancellationToken cancellationToken);

    Task<Result> HandleRequestAsync(IRequest request, CancellationToken cancellationToken);
}
