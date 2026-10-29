using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Sentinel.Contracts;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Observations;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Queries;

namespace Sentinel.AgentWorker.Application.Tools;

/// <summary>
/// A tool's answer: the content returned to the model, the observation IDs it contained (the only
/// IDs findings may cite) and whether the data behind it was partial.
/// </summary>
public sealed record ToolOutcome(string Content, IReadOnlyList<ObservationId> ObservationIds, bool Partial, string? Error = null)
{
    public static ToolOutcome Failure(string error) =>
        new(JsonSerializer.Serialize(new { error }, SentinelJson.Options), [], true, error);
}
