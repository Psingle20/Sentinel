namespace Sentinel.Contracts.Primitives.Identifiers;

public interface IStronglyTypedId<TSelf, TValue>
    where TSelf : struct, IStronglyTypedId<TSelf, TValue>
    where TValue : notnull
{
    TValue Value { get; }

    static abstract TSelf From(TValue value);
}
