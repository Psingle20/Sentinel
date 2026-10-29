using Sentinel.Contracts.Investigations;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Incidents;

/// <summary>What the incident concluded was the cause, if it concluded anything.</summary>
public sealed record MemoryCause(string? Key, string Statement, ServiceId? ServiceId, double Confidence, string? ChangeDeploymentId);
