using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.BuildingBlocks.Tests.TestSupport;

internal sealed class LogSink
{
    public ConcurrentQueue<LogEntry> Entries { get; } = new();
}
