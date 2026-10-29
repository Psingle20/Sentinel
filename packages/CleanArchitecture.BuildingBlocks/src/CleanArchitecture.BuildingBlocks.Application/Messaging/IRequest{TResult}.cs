namespace CleanArchitecture.BuildingBlocks.Messaging;

/// <summary>
/// An inbound application request that produces <typeparamref name="TResult"/>. Exactly one
/// <see cref="IProcessor{TRequest, TResult}"/> accepts it, sent through <see cref="IRequestDispatcher"/>.
/// </summary>
public interface IRequest<TResult>;
