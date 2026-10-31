using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Runtime.Domain.Incidents;

/// <summary>
/// Text an investigator's untrusted tool results held that read as an instruction to an AI agent, as AgentWorker reported
/// it. The incident records it so that people see the attempt; nothing in the incident ever acts on it.
/// </summary>
public sealed record UntrustedInstruction(string Tool, string Source, ServiceId? ServiceId, string Excerpt, string Pattern);
