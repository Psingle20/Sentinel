namespace CleanArchitecture.BuildingBlocks.Messaging;

/// <summary>
/// An inbound application request with no result: what an HTTP endpoint, an integration-message
/// consumer or a workflow activity asks the application to do. Exactly one
/// <see cref="IProcessor{TRequest}"/> accepts it, sent through <see cref="IRequestDispatcher"/>.
/// A request carries no transport types and never crosses a service boundary.
/// </summary>
public interface IRequest;
