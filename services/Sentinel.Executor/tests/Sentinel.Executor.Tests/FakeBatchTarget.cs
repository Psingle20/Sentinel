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

/// <summary>A batch target whose pause can be undone, and can be told to refuse.</summary>
internal sealed class FakeBatchTarget : IRemediationAdapter
{
    public bool Active { get; set; } = true;

    public bool RefusePause { get; set; }

    public int Resumes { get; private set; }

    public string ActionKind => "pause-batch";

    public IReadOnlyDictionary<string, string> CapabilityLimits(RemediationAction action) => new Dictionary<string, string>(StringComparer.Ordinal);

    public Task<TargetInspection> InspectAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(new TargetInspection(Active ? "active" : "inactive", Active ? OperationStatus.InProgress : OperationStatus.Completed, Active ? "active" : "inactive"));

    public Task<AdapterResult> ExecuteAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (RefusePause)
        {
            return Task.FromResult(new AdapterResult(AdapterOutcome.Rejected, "pause refused"));
        }

        Active = false;
        return Task.FromResult(new AdapterResult(AdapterOutcome.Accepted, "paused"));
    }

    public Task<bool> VerifyActionAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken) => Task.FromResult(!Active);

    public Task<AdapterResult> CompensateAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken)
    {
        Resumes++;
        Active = true;
        return Task.FromResult(new AdapterResult(AdapterOutcome.Accepted, "resumed"));
    }
}
