using System.Text.Json.Serialization;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Observations;

public enum LogSeverity
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}
