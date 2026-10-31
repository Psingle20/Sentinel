using System.Text.Json.Serialization;
using Sentinel.Contracts.Incidents;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;

namespace Sentinel.Contracts.Remediation;

/// <summary>A typed remediation action. Free text can never name an executable capability (Phase 04).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RollbackAction), "rollback")]
[JsonDerivedType(typeof(PauseBatchAction), "pause-batch")]
[JsonDerivedType(typeof(QuarantineMessageAction), "quarantine-message")]
[JsonDerivedType(typeof(RestoreRouteAction), "restore-route")]
public abstract record RemediationAction
{
    public required ActionId ActionId { get; init; }

    public required ServiceId TargetServiceId { get; init; }
}
