using System.Security.Cryptography;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.Abstractions.Remediation;

/// <summary>
/// A privileged remediation adapter (ADR-006). Only Executor holds the credentials adapters use.
/// <see cref="ExecuteAsync"/> is idempotent on the request's idempotency key; <see cref="InspectAsync"/>
/// reports what actually happened and drives reconciliation.
/// </summary>
public interface IRemediationAdapter
{
    string ActionKind { get; }

    /// <summary>The limits a capability for this action carries, for example how far a rollback may move.</summary>
    IReadOnlyDictionary<string, string> CapabilityLimits(RemediationAction action);

    Task<TargetInspection> InspectAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Performs the action. Refuses unless <paramref name="capability"/> authorizes exactly this action now.</summary>
    Task<AdapterResult> ExecuteAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>True when the action took effect at the target, for example the previous revision is serving.</summary>
    Task<bool> VerifyActionAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>Undoes a completed action during compensation, under its own single-use capability.</summary>
    Task<AdapterResult> CompensateAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken);
}
