using System.Security.Cryptography;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;

namespace Sentinel.Executor.Application.Abstractions.Persistence;

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
