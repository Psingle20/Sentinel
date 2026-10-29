using CleanArchitecture.BuildingBlocks.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.BuildingBlocks.Infrastructure.Tests;

/// <summary>The inbound processor's logger, recording the scopes it opens.</summary>
internal sealed class ScopeRecordingLogger : ILogger<InboundMessageProcessor>
{
    private readonly List<IReadOnlyDictionary<string, object?>> _scopes = [];

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> Scopes
    {
        get
        {
            lock (_scopes)
            {
                return [.. _scopes];
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        if (state is IEnumerable<KeyValuePair<string, object?>> fields)
        {
            lock (_scopes)
            {
                _scopes.Add(fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal));
            }
        }

        return null;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
    }
}
