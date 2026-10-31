namespace Reference.Common.Control;

/// <summary>
/// A log line an attacker has planted in a service: while it lasts, the service logs it as one of its own warnings, as text
/// that reached its logs from a request or a dependency would be. The prompt-injection scenario plants an instruction to
/// AI agents this way, to show that Sentinel reads it as data. Development-only, behind the failure controls.
/// </summary>
public sealed class PlantedLog(TimeProvider time)
{
    private readonly Lock _gate = new();
    private string? _message;
    private DateTimeOffset _until;

    /// <summary>The message to log now, or null when nothing is planted or it has run out.</summary>
    public string? Current
    {
        get
        {
            lock (_gate)
            {
                return _message is not null && time.GetUtcNow() < _until ? _message : null;
            }
        }
    }

    public void Plant(string message, int seconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seconds);

        lock (_gate)
        {
            _message = message;
            _until = time.GetUtcNow().AddSeconds(seconds);
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _message = null;
        }
    }
}
