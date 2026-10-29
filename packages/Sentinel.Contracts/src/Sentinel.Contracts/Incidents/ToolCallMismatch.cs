using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>A tool call whose replayed result differed from the recorded one.</summary>
public sealed record ToolCallMismatch(string ToolCallId, string ToolName, string Reason);
