namespace Reference.Scenarios.Application.Abstractions;

/// <summary>An environment operation that failed, and why.</summary>
public sealed class ReferenceEnvironmentException : Exception
{
    public ReferenceEnvironmentException()
    {
    }

    public ReferenceEnvironmentException(string message)
        : base(message)
    {
    }

    public ReferenceEnvironmentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ReferenceEnvironmentException(EnvironmentFailure failure, string message)
        : base(message) => Failure = failure;

    public EnvironmentFailure Failure { get; }
}
