using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.BuildingBlocks.Tests.TestSupport;

internal sealed record LogEntry(LogLevel Level, string Message, IReadOnlyList<object?> Scopes);

internal sealed class LogSink
{
    public ConcurrentQueue<LogEntry> Entries { get; } = new();
}

internal sealed class CapturingLogger<T>(LogSink sink) : ILogger<T>
{
    private readonly List<object?> _scopes = [];

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        _scopes.Add(state);
        return new Scope(() => _scopes.Remove(state));
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        sink.Entries.Enqueue(new LogEntry(logLevel, formatter(state, exception), [.. _scopes]));
    }

    private sealed class Scope(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
