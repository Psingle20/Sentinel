namespace CleanArchitecture.BuildingBlocks.Messaging;

/// <summary>
/// The application entry point for one request type: it maps the request to exactly one command or
/// query and dispatches it. The request's validators run before <see cref="Process"/>, so a
/// processor depends only on a dispatcher. It never calls ports, loads aggregates or publishes.
/// </summary>
public interface IProcessor<in TRequest>
    where TRequest : IRequest
{
    Task<Result> Process(TRequest request, CancellationToken cancellationToken);
}
