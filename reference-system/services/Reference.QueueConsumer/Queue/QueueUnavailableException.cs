namespace Reference.QueueConsumer.Queue;

/// <summary>The orders queue could not be reached; the caller retries.</summary>
public sealed class QueueUnavailableException : Exception
{
    public QueueUnavailableException()
    {
    }

    public QueueUnavailableException(string message)
        : base(message)
    {
    }

    public QueueUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
