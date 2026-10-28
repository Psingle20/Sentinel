using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Domain.Executions;

/// <summary>One provider response an execution received, exactly as it was returned (Phase 07).</summary>
public sealed record CapturedResponse(int Sequence, string Kind, string RequestHash, string Request, string Response);

/// <summary>
/// Everything an execution was given: the command it answered and every provider response its tools
/// read, so it can be replayed without calling a live provider (Phase 07, replay levels 2 and 3).
/// </summary>
public sealed record ExecutionInputs
{
    public required AgentExecutionId AgentExecutionId { get; init; }

    /// <summary>The investigation command, in contract JSON.</summary>
    public required string Command { get; init; }

    public IReadOnlyList<CapturedResponse> Responses { get; init; } = [];

    public required string ToolCatalogVersion { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }
}
