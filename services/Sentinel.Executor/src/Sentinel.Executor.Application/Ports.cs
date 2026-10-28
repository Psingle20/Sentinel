using System.Security.Cryptography;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application;

/// <summary>Executor's durable execution store. Every method runs inside the current unit of work.</summary>
public interface IExecutionStore
{
    Task<ExecutionRequest?> FindAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task SaveAsync(ExecutionRequest request, CancellationToken cancellationToken);

    /// <summary>Requests without a final outcome that last changed before <paramref name="olderThan"/>: candidates for reconciliation.</summary>
    Task<IReadOnlyList<ExecutionRequest>> UnfinishedAsync(DateTimeOffset olderThan, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExecutionRequest>> RecentAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Records an attestation as used. Returns false when it was already consumed: an attestation is single-use.</summary>
    Task<bool> TryConsumeAttestationAsync(AttestationId attestationId, Guid requestId, CancellationToken cancellationToken);

    /// <summary>Raises the incident's fence to <paramref name="fencingToken"/>; returns false when a higher token was already seen.</summary>
    Task<bool> TryRaiseFenceAsync(IncidentId incidentId, long fencingToken, CancellationToken cancellationToken);

    /// <summary>Records an issued capability for audit.</summary>
    Task SaveCapabilityAsync(Capability capability, CancellationToken cancellationToken);

    /// <summary>Marks a capability used. Returns false when it was already used: a capability is single-use.</summary>
    Task<bool> TryConsumeCapabilityAsync(Guid capabilityId, CancellationToken cancellationToken);
}

/// <summary>The versioned remediation policy document (policy as code).</summary>
public interface IPolicyStore
{
    PolicyDocument Current { get; }
}

/// <summary>Operator controls: the emergency stop and per-action disable switches, persisted apart from any model path.</summary>
public interface IControlStore
{
    Task<ExecutorControls> GetAsync(CancellationToken cancellationToken);

    Task SetEmergencyStopAsync(bool engaged, string actor, CancellationToken cancellationToken);

    Task SetActionDisabledAsync(string actionKind, bool disabled, string actor, CancellationToken cancellationToken);
}

/// <summary>Resolves Api's approval verification keys by key ID (ADR-006). Executor holds no signing key.</summary>
public interface IApprovalKeyResolver
{
    Task<ECDsa?> ResolveAsync(string keyId, CancellationToken cancellationToken);
}

/// <summary>Runtime's start confirmation. Throws when Runtime cannot be reached, which fails the execution closed.</summary>
public interface IStartConfirmation
{
    Task<RemediationStartResponse> ConfirmAsync(RemediationStartRequest request, CancellationToken cancellationToken);
}

public enum AdapterOutcome
{
    Accepted,
    Rejected,
    Unknown
}

public sealed record AdapterResult(AdapterOutcome Outcome, string Detail);

public enum OperationStatus
{
    NotFound,
    InProgress,
    Completed,
    Failed
}

/// <summary>The target's actual state: what is serving, and what became of this request's operation.</summary>
public sealed record TargetInspection(string? ServingRevision, OperationStatus Operation, string Detail);

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
