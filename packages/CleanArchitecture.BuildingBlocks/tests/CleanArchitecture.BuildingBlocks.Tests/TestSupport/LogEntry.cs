using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CleanArchitecture.BuildingBlocks.Tests.TestSupport;

internal sealed record LogEntry(LogLevel Level, string Message, IReadOnlyList<object?> Scopes);
