using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>Roll a service back from its current revision to a previous one.</summary>
public sealed record RollbackAction : RemediationAction
{
    public required string FromRevision { get; init; }

    public required string ToRevision { get; init; }
}
