using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CleanArchitecture.BuildingBlocks.Integration;
using Sentinel.Contracts.Messaging;
using Sentinel.Contracts.Primitives.Identifiers;
using Sentinel.Contracts.Remediation;
using Sentinel.Executor.Application;
using Sentinel.Executor.Domain.Capabilities;
using Sentinel.Executor.Domain.Executions;
using Sentinel.Executor.Domain.Policy;
using Sentinel.Executor.Application.Abstractions.Persistence;
using Sentinel.Executor.Application.Abstractions.Providers;
using Sentinel.Executor.Application.Abstractions.Remediation;
using Sentinel.Executor.Application.Abstractions.Security;

namespace Sentinel.Executor.Tests;

internal sealed class InMemoryExecutionStore : IExecutionStore
{
    private readonly Dictionary<string, ExecutionSnapshot> _requests = new(StringComparer.Ordinal);
    private readonly HashSet<AttestationId> _consumed = [];
    private readonly Dictionary<IncidentId, long> _fences = [];
    private readonly Dictionary<Guid, (Capability Capability, bool Consumed)> _capabilities = [];

    public IReadOnlyCollection<Capability> Capabilities => [.. _capabilities.Values.Select(entry => entry.Capability)];

    public Task SaveCapabilityAsync(Capability capability, CancellationToken cancellationToken)
    {
        _capabilities.TryAdd(capability.CapabilityId, (capability, false));
        return Task.CompletedTask;
    }

    public Task<bool> TryConsumeCapabilityAsync(Guid capabilityId, CancellationToken cancellationToken)
    {
        if (!_capabilities.TryGetValue(capabilityId, out (Capability Capability, bool Consumed) entry) || entry.Consumed)
        {
            return Task.FromResult(false);
        }

        _capabilities[capabilityId] = (entry.Capability, true);
        return Task.FromResult(true);
    }

    public IReadOnlyCollection<ExecutionSnapshot> All => _requests.Values;

    public Task<ExecutionRequest?> FindAsync(string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(_requests.TryGetValue(idempotencyKey, out ExecutionSnapshot? snapshot) ? ExecutionRequest.Restore(snapshot) : null);

    public Task SaveAsync(ExecutionRequest request, CancellationToken cancellationToken)
    {
        _requests[request.IdempotencyKey] = request.Snapshot();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExecutionRequest>> UnfinishedAsync(DateTimeOffset olderThan, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExecutionRequest>>([.. _requests.Values.Select(ExecutionRequest.Restore).Where(request => !request.HasFinalOutcome)]);

    public Task<IReadOnlyList<ExecutionRequest>> RecentAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExecutionRequest>>([.. _requests.Values.Select(ExecutionRequest.Restore)]);

    public Task<bool> TryConsumeAttestationAsync(AttestationId attestationId, Guid requestId, CancellationToken cancellationToken) =>
        Task.FromResult(_consumed.Add(attestationId));

    public Task<bool> TryRaiseFenceAsync(IncidentId incidentId, long fencingToken, CancellationToken cancellationToken)
    {
        if (_fences.TryGetValue(incidentId, out long highest) && highest > fencingToken)
        {
            return Task.FromResult(false);
        }

        _fences[incidentId] = fencingToken;
        return Task.FromResult(true);
    }
}
