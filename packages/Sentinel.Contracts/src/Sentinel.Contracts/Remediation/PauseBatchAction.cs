using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>Pause a service's background batch work; compensated by resuming it (Phase 06).</summary>
public sealed record PauseBatchAction : RemediationAction
{
    /// <summary>Why the batch is paused, for the operator who must resume it.</summary>
    public required string Reason { get; init; }
}
