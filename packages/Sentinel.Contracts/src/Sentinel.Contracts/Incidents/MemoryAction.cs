using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>What was done about the incident and how it ended.</summary>
public sealed record MemoryAction(string Kind, ServiceId TargetServiceId, string Summary, string PolicyDecision, string? ExecutionOutcome, string? RecoveryOutcome);
