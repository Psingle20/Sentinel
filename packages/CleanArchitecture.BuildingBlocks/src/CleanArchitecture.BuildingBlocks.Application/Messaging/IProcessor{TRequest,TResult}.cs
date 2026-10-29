namespace CleanArchitecture.BuildingBlocks.Messaging;

/// <summary>
/// The application entry point for one request type with a result: it maps the request to exactly
/// one command or query and dispatches it. The request's validators run before <see cref="Process"/>.
/// </summary>
public interface IProcessor<in TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<Result<TResult>> Process(TRequest request, CancellationToken cancellationToken);
}
