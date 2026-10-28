using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Sentinel.EndToEnd.Tests;

/// <summary>
/// Keeps the most recent log lines of every resource. Aspire's testing host forwards each
/// resource's console output to loggers named <c>{AppHost}.Resources.{resource}</c>; this provider
/// records them so a failing end-to-end test can print what each service was doing.
/// </summary>
public sealed class ResourceLogs : ILoggerProvider
{
    private const int LinesPerResource = 500;
    private const string ResourcesCategory = ".Resources.";

    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _lines = new(StringComparer.Ordinal);

    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);

        int index = categoryName.IndexOf(ResourcesCategory, StringComparison.Ordinal);
        return index < 0
            ? NullCapture.Instance
            : new Capture(_lines.GetOrAdd(categoryName[(index + ResourcesCategory.Length)..], _ => new ConcurrentQueue<string>()));
    }

    /// <summary>The last <paramref name="count"/> lines of one resource, optionally only those containing <paramref name="filter"/>.</summary>
    public IReadOnlyList<string> Tail(string resource, int count = 60, string? filter = null) =>
        _lines.TryGetValue(resource, out ConcurrentQueue<string>? lines)
            ? [.. lines.Where(line => filter is null || line.Contains(filter, StringComparison.OrdinalIgnoreCase)).TakeLast(count)]
            : [];

    public string Dump(IEnumerable<string> resources, int count = 40) =>
        string.Join(Environment.NewLine, resources.Select(resource =>
            $"--- {resource}{Environment.NewLine}{string.Join(Environment.NewLine, Tail(resource, count))}"));

    public void Dispose()
    {
        // Nothing to release; lines are kept in memory for the test run.
    }

    private sealed class Capture(ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            lines.Enqueue(formatter(state, exception));
            while (lines.Count > LinesPerResource)
            {
                lines.TryDequeue(out _);
            }
        }
    }

    private sealed class NullCapture : ILogger
    {
        public static readonly NullCapture Instance = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Only resource output is captured.
        }
    }
}
