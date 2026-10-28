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

namespace Sentinel.Executor.Tests;

/// <summary>Plays Api's signer: an in-memory key whose public half the resolver publishes.</summary>
internal sealed class TestSigner : IApprovalKeyResolver, IDisposable
{
    public const string KeyId = "test-key-1";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public Task<ECDsa?> ResolveAsync(string keyId, CancellationToken cancellationToken) =>
        Task.FromResult(keyId == KeyId ? _key : null);

    public string Sign(ApprovalAttestationClaims claims, string keyId = KeyId, ECDsa? key = null)
    {
        string header = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new ApprovalAttestationHeader(ApprovalAttestation.Algorithm, keyId, ApprovalAttestation.TokenType), SentinelJson.Options));
        string payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(claims, SentinelJson.Options));
        byte[] signature = (key ?? _key).SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256);
        return $"{header}.{payload}.{Base64Url.EncodeToString(signature)}";
    }

    public void Dispose() => _key.Dispose();
}

internal static class Commands
{
    public static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 10, 0, TimeSpan.Zero);
    public static readonly TenantId Tenant = TenantId.From("acme");
    public static readonly EnvironmentId Environment = EnvironmentId.From("reference");

    public static RemediationPlan Plan(string target = "checkout-service")
    {
        var draft = new RemediationPlan
        {
            PlanId = PlanId.New(),
            PlanVersion = 1,
            PlanHash = string.Empty,
            HypothesisId = HypothesisId.New(),
            Actions = [new RollbackAction { ActionId = ActionId.New(), TargetServiceId = ServiceId.From(target), FromRevision = "v2.4.1", ToRevision = "v2.4.0" }],
            ExpectedRecovery = [],
            ObservationWindow = TimeSpan.FromSeconds(90),
            Risk = RiskLevel.Medium
        };
        return draft with { PlanHash = RemediationPlanHash.Compute(draft) };
    }

    /// <summary>Pauses the reporting batch; with <paramref name="alsoRollback"/>, then rolls checkout back.</summary>
    public static RemediationPlan PausePlan(bool alsoRollback = false)
    {
        RemediationAction pause = new PauseBatchAction { ActionId = ActionId.New(), TargetServiceId = ServiceId.From("reporting-worker"), Reason = "Saturates Redis." };
        RemediationAction rollback = new RollbackAction { ActionId = ActionId.New(), TargetServiceId = ServiceId.From("checkout-service"), FromRevision = "v2.4.1", ToRevision = "v2.4.0" };
        return Hashed(alsoRollback ? [pause, rollback] : [pause], RiskLevel.Low);
    }

    public static RemediationPlan RollbackThenPause() => Hashed(
    [
        new RollbackAction { ActionId = ActionId.New(), TargetServiceId = ServiceId.From("checkout-service"), FromRevision = "v2.4.1", ToRevision = "v2.4.0" },
        new PauseBatchAction { ActionId = ActionId.New(), TargetServiceId = ServiceId.From("reporting-worker"), Reason = "Saturates Redis." }
    ], RiskLevel.Low);

    private static RemediationPlan Hashed(IReadOnlyList<RemediationAction> actions, RiskLevel risk)
    {
        var draft = new RemediationPlan
        {
            PlanId = PlanId.New(),
            PlanVersion = 1,
            PlanHash = string.Empty,
            HypothesisId = HypothesisId.New(),
            Actions = actions,
            ExpectedRecovery = [],
            ObservationWindow = TimeSpan.FromSeconds(90),
            Risk = risk
        };
        return draft with { PlanHash = RemediationPlanHash.Compute(draft) };
    }

    public static ApprovalAttestationClaims Claims(IncidentId incident, RemediationPlan plan) => new()
    {
        AttestationId = AttestationId.New(),
        TenantId = Tenant,
        EnvironmentId = Environment,
        IncidentId = incident,
        PlanId = plan.PlanId,
        PlanVersion = plan.PlanVersion,
        PlanHash = plan.PlanHash,
        ApproverId = "responder@example.test",
        ApprovalScope = ApprovalAttestation.ExecutePlanScope,
        PolicyVersion = "static-2026.10.1",
        IssuedAt = Now.AddMinutes(-1),
        ExpiresAt = Now.AddMinutes(14),
        Issuer = ApprovalAttestation.Issuer,
        Audience = ApprovalAttestation.Audience
    };

    public static ExecuteRemediation Execute(TestSigner signer, RemediationPlan? plan = null, long fencingToken = 7, string? key = null, IncidentId? incident = null, Func<ApprovalAttestationClaims, ApprovalAttestationClaims>? claims = null)
    {
        RemediationPlan approved = plan ?? Plan();
        IncidentId incidentId = incident ?? IncidentId.New();
        ApprovalAttestationClaims signed = Claims(incidentId, approved);
        return new ExecuteRemediation
        {
            TenantId = Tenant,
            EnvironmentId = Environment,
            IncidentId = incidentId,
            Plan = approved,
            Attestation = signer.Sign(claims is null ? signed : claims(signed)),
            FencingToken = fencingToken,
            IdempotencyKey = key ?? $"acme:{incidentId}:execute:1",
            RequestedAt = Now
        };
    }
}

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

internal sealed class FakeStartConfirmation : IStartConfirmation
{
    public bool Confirm { get; set; } = true;

    public bool Unreachable { get; set; }

    public int Calls { get; private set; }

    public Task<RemediationStartResponse> ConfirmAsync(RemediationStartRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        if (Unreachable)
        {
            throw new HttpRequestException("Runtime is down.");
        }

        return Task.FromResult(new RemediationStartResponse(Confirm, Confirm ? null : "The plan was superseded."));
    }
}

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

/// <summary>The reference policy as the host ships it, replaceable per test.</summary>
internal sealed class FakePolicyStore : IPolicyStore
{
    public PolicyDocument Current { get; set; } = Reference();

    public static PolicyDocument Reference() => new()
    {
        Version = "policy-test",
        DefaultMode = AutonomyMode.ApprovalRequired,
        Rules =
        [
            new PolicyRule { Id = "rollback.reference", Action = "rollback", Environments = ["reference"], Targets = ["checkout-service", "order-service"], MaxRisk = "medium", MinConfidence = 0.5, MaxBlastRadius = 2, Grants = AutonomyMode.ApprovalRequired },
            new PolicyRule { Id = "pause-batch.reference", Action = "pause-batch", Environments = ["reference"], MaxRisk = "low", Grants = AutonomyMode.SuggestionOnly }
        ]
    };
}

internal sealed class FakeControlStore : IControlStore
{
    public ExecutorControls Controls { get; set; } = ExecutorControls.None;

    public Task<ExecutorControls> GetAsync(CancellationToken cancellationToken) => Task.FromResult(Controls);

    public Task SetEmergencyStopAsync(bool engaged, string actor, CancellationToken cancellationToken)
    {
        Controls = Controls with { EmergencyStop = engaged };
        return Task.CompletedTask;
    }

    public Task SetActionDisabledAsync(string actionKind, bool disabled, string actor, CancellationToken cancellationToken)
    {
        HashSet<string> set = [.. Controls.DisabledActions];
        if (disabled)
        {
            set.Add(actionKind);
        }
        else
        {
            set.Remove(actionKind);
        }

        Controls = Controls with { DisabledActions = set };
        return Task.CompletedTask;
    }
}

internal sealed class RecordingOutbox : IOutbox
{
    public List<object> Messages { get; } = [];

    public void Enqueue<TMessage>(TMessage message, MessageMetadata? metadata = null)
        where TMessage : class => Messages.Add(message);
}

internal sealed class NoUnitOfWork : IUnitOfWork
{
    public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
