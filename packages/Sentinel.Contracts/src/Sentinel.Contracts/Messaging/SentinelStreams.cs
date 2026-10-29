using System.Text.Json;
using System.Text.Json.Serialization;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Messaging;

/// <summary>Stream names, following the topic families in architecture §56.</summary>
public static class SentinelStreams
{
    public const string Observations = "sentinel.observations";
    public const string Incidents = "sentinel.incidents";
    public const string Investigations = "sentinel.investigations";
    public const string Remediation = "sentinel.remediation";
    public const string AgentWorkerCommands = "sentinel.agentworker.commands";
    public const string RuntimeCommands = "sentinel.runtime.commands";
    public const string ExecutorCommands = "sentinel.executor.commands";
}
