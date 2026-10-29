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

/// <summary>A rollback target: serves <see cref="Serving"/>, and records every call made to it.</summary>
internal sealed class FakeRollbackTarget : IRemediationAdapter
{
    public string Serving { get; set; } = "v2.4.1";

    public AdapterOutcome NextOutcome { get; set; } = AdapterOutcome.Accepted;

    public OperationStatus Operation { get; set; } = OperationStatus.NotFound;

    /// <summary>When true, an accepted rollback takes effect at once.</summary>
    public bool AppliesImmediately { get; set; } = true;

    public int Executions { get; private set; }

    public List<Capability> Capabilities { get; } = [];

    public string ActionKind => "rollback";

    public IReadOnlyDictionary<string, string> CapabilityLimits(RemediationAction action) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["to"] = ((RollbackAction)action).ToRevision };

    public Task<TargetInspection> InspectAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(new TargetInspection(Serving, Operation, $"serving {Serving}, operation {Operation}"));

    public Task<AdapterResult> ExecuteAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken)
    {
        Executions++;
        Capabilities.Add(capability);
        if (NextOutcome == AdapterOutcome.Accepted && AppliesImmediately)
        {
            Serving = ((RollbackAction)action).ToRevision;
            Operation = OperationStatus.Completed;
        }

        return Task.FromResult(new AdapterResult(NextOutcome, $"rollback {NextOutcome}"));
    }

    public Task<bool> VerifyActionAsync(RemediationAction action, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(Operation == OperationStatus.Completed && Serving == ((RollbackAction)action).ToRevision);

    public Task<AdapterResult> CompensateAsync(RemediationAction action, Capability capability, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(new AdapterResult(AdapterOutcome.Rejected, "not supported"));
}
