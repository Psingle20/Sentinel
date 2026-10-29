using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.AgentWorker.Domain.Executions;

/// <summary>One provider response an execution received, exactly as it was returned (Phase 07).</summary>
public sealed record CapturedResponse(int Sequence, string Kind, string RequestHash, string Request, string Response);
