using System.Security.Cryptography;
using System.Text.Json;
using Sentinel.AgentWorker.Domain.Executions;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Application.Tools;

/// <summary>The version of the tool catalog investigators call; recorded with every execution (Phase 07).</summary>
public static class ToolCatalog
{
    public const string Version = "tools-v2";
}
